using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using IslandGame.Building;
using IslandGame.Data.Building;
using IslandGame.Data.Items;
using IslandGame.Inventory;
using IslandGame.Player;
using IslandGame.Sky;
using IslandGame.Stats;
using IslandGame.Terrain;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IslandGame.Saving
{
    /// <summary>
    /// The persistence orchestrator: Save() queries every system's state
    /// through the small public accessors added for this phase; Load()
    /// reloads the scene and restores in two passes timed around Unity's
    /// lifecycle:
    ///
    ///   PRE-START (sceneLoaded callback — after Awake, before any Start):
    ///     world seed (the generator derives noise offsets in Start),
    ///     terrain deltas registered with VoxelWorld (each applies when its
    ///     chunk streams in — loading never blocks on the world), world time,
    ///     and the structure system's processed cells (its first tick could
    ///     otherwise re-place looted ruins).
    ///   POST-START (one frame later): player pose/inventory/stats, placed
    ///     pieces + functional state, autosave clock reset. This ordering
    ///     matters because InventorySystem.Start seeds starting items and
    ///     StatContainer builds instances — restoring before Start would be
    ///     overwritten.
    ///
    /// TERRAIN DELTAS, not full chunks: untouched chunks regenerate
    /// deterministically from the seed, so saving them is pure redundancy —
    /// a full 12×12-chunk session would be ~4.5 MB of block data versus a
    /// few KB of actual edits. At save time each IsModified chunk is diffed
    /// against a freshly regenerated baseline (a 16×64×16 ushort compare,
    /// microseconds per chunk); promoted sub-voxel cells save their bitsets
    /// wholesale.
    ///
    /// CREATURE POLICY — wild creatures are NOT persisted, spawners simply
    /// repopulate on load. Justification: creatures are already ephemeral by
    /// design (pooled, despawned beyond 90 m, night broods cleared at dawn),
    /// carry no player investment, and per-creature persistence would buy
    /// visual continuity nobody can verify while costing save size and a
    /// spawn-reconciliation pass. The moment a taming/naming system exists,
    /// THOSE creatures become player investment and go in the reserved
    /// SaveGame.ownedCreatures list — flagged, not built speculatively.
    ///
    /// FILES: persistentDataPath/Saves/{slot}.json. Autosave writes the
    /// "autosave" slot on a configurable interval through the same Save path.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SaveManager : MonoBehaviour
    {
        [Tooltip("Minutes between autosaves to the 'autosave' slot. 0 = autosave off.")]
        [Range(0f, 60f)]
        [SerializeField] private float autosaveIntervalMinutes = 5f;

        public const string AutosaveSlot = "autosave";

        private static SaveManager instance;

        private SaveGame pendingLoad;
        private float nextAutosaveTime;

        /// <summary>The scene singleton (created by the Save System builder).</summary>
        public static SaveManager Instance => instance;

        /// <summary>True while a loaded game is being applied (systems can ignore transient events).</summary>
        public bool IsApplyingLoad { get; private set; }

        public static string SaveFolder => Path.Combine(Application.persistentDataPath, "Saves");

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                // A loaded scene re-created the builder's object while the
                // DontDestroyOnLoad original persists — keep the original.
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
            ResetAutosaveClock();
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
                SceneManager.sceneLoaded -= OnSceneLoaded;
            }
        }

        private void Update()
        {
            if (autosaveIntervalMinutes <= 0f || IsApplyingLoad)
                return;

            if (Time.time >= nextAutosaveTime)
            {
                ResetAutosaveClock();
                Save(AutosaveSlot);
            }
        }

        private void ResetAutosaveClock()
        {
            nextAutosaveTime = Time.time + autosaveIntervalMinutes * 60f;
        }

        // ==================================================================
        // SAVE
        // ==================================================================

        /// <summary>Gathers the full game state and writes it to the slot. Returns success.</summary>
        public bool Save(string slotName)
        {
            var world = FindFirstObjectByType<VoxelWorld>();
            var player = FindFirstObjectByType<PlayerReferences>();
            if (world == null || !world.IsReady || world.ActiveIslandGenerator == null || player == null)
            {
                Debug.LogWarning("[Save] World or player not ready — nothing saved.");
                return false;
            }

            var save = new SaveGame
            {
                savedAtUtc = DateTime.UtcNow.ToString("o"),
                worldSeed = world.ActiveIslandGenerator.Seed,
            };

            var timeOfDay = FindFirstObjectByType<TimeOfDayController>();
            if (timeOfDay != null)
            {
                save.timeOfDay01 = timeOfDay.TimeOfDay01;
                save.dayNumber = timeOfDay.DayNumber;
            }

            WritePlayer(save.player, player);
            WriteTerrain(save.terrain, world);
            WritePieces(save.pieces);
            WriteStructureCells(save.structureCells);
            WriteGuardSpawners(save.guardSpawners);

            try
            {
                Directory.CreateDirectory(SaveFolder);
                File.WriteAllText(GetSlotPath(slotName), JsonUtility.ToJson(save));
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Save] Writing slot '{slotName}' failed: {exception.Message}");
                return false;
            }

            Debug.Log(
                $"[Save] Slot '{slotName}': {save.terrain.Count} modified chunk(s), {save.pieces.Count} piece(s), " +
                $"{save.player.inventory.Count} inventory slot(s). → {GetSlotPath(slotName)}");
            return true;
        }

        private static void WritePlayer(SavedPlayer data, PlayerReferences player)
        {
            data.position = player.transform.position;
            data.yaw = player.transform.eulerAngles.y;
            data.pitch = player.CameraController != null ? player.CameraController.Pitch : 0f;

            var selector = player.GetComponent<HotbarSelector>();
            data.selectedHotbarIndex = selector != null ? selector.SelectedIndex : 0;

            var inventory = player.GetComponent<InventorySystem>();
            if (inventory != null)
                WriteInventory(data.inventory, inventory);

            var stats = player.GetComponent<StatContainer>();
            if (stats != null)
            {
                data.stats.Clear();
                var instances = stats.Instances;
                for (int i = 0; i < instances.Count; i++)
                {
                    data.stats.Add(new SavedStat
                    {
                        statId = instances[i].Definition.Id,
                        current = instances[i].Current,
                    });
                }
            }
        }

        private static void WriteInventory(List<SavedItemSlot> output, InventorySystem inventory)
        {
            output.Clear();
            for (int i = 0; i < inventory.SlotCount; i++)
            {
                InventorySlot slot = inventory.GetSlot(i);
                output.Add(new SavedItemSlot
                {
                    itemId = slot.IsEmpty ? string.Empty : slot.Item.Id,
                    count = slot.Count,
                    durability01 = slot.Durability01,
                });
            }
        }

        /// <summary>Delta extraction: diff each modified chunk against a regenerated baseline; promotions save their bitsets.</summary>
        private static void WriteTerrain(List<SavedChunkDelta> output, VoxelWorld world)
        {
            output.Clear();
            BlockPalette palette = world.Palette;

            foreach (Chunk chunk in world.LoadedChunks)
            {
                if (!chunk.IsModified)
                    continue;

                Chunk baseline = world.GenerateBaselineChunk(chunk.Coord);
                var delta = new SavedChunkDelta { coordX = chunk.Coord.x, coordZ = chunk.Coord.y };
                var tableLookup = new Dictionary<string, int>(8);

                for (int y = 0; y < Chunk.SizeY; y++)
                {
                    for (int z = 0; z < Chunk.SizeZ; z++)
                    {
                        for (int x = 0; x < Chunk.SizeX; x++)
                        {
                            ushort current = chunk.Get(x, y, z);
                            if (current == baseline.Get(x, y, z))
                                continue;

                            string blockId = current == BlockPalette.AirId
                                ? string.Empty
                                : palette.GetDefinition(current)?.Id ?? string.Empty;

                            if (!tableLookup.TryGetValue(blockId, out int tableIndex))
                            {
                                tableIndex = delta.blockIdTable.Count;
                                delta.blockIdTable.Add(blockId);
                                tableLookup.Add(blockId, tableIndex);
                            }

                            delta.cellIndices.Add(Chunk.FlattenIndex(x, y, z));
                            delta.blockIdTableIndices.Add(tableIndex);
                        }
                    }
                }

                foreach (KeyValuePair<int, SubVoxelGrid> promotion in chunk.PromotedCells)
                {
                    delta.promotions.Add(new SavedPromotedCell
                    {
                        cellIndex = promotion.Key,
                        resolution = promotion.Value.Resolution,
                        bitsBase64 = Convert.ToBase64String(promotion.Value.ExportBits()),
                    });
                }

                if (delta.cellIndices.Count > 0 || delta.promotions.Count > 0)
                    output.Add(delta);
            }
        }

        private static void WritePieces(List<SavedPiece> output)
        {
            output.Clear();
            PlacedPieceRegistry registry = PlacedPieceRegistry.Instance;
            if (registry == null)
                return;

            foreach (BuildingPiece piece in registry.All)
            {
                if (piece == null || !piece.IsInitialized || piece.Definition == null)
                    continue;

                var data = new SavedPiece
                {
                    pieceId = piece.Definition.Id,
                    position = piece.transform.position,
                    rotation = piece.transform.rotation,
                    health = piece.CurrentHealth,
                };

                var campfire = piece.GetComponentInChildren<CampfireBehavior>(true);
                if (campfire != null)
                {
                    campfire.GetSaveState(out data.campfireFuel, out data.campfireLit);
                }

                var door = piece.GetComponentInChildren<DoorBehavior>(true);
                if (door != null)
                {
                    data.hasDoorState = true;
                    data.doorOpen = door.IsOpen;
                    data.rotation = door.PersistedRotation; // an open door's transform is mid-swing
                }

                var chest = piece.GetComponentInChildren<ChestBehavior>(true);
                if (chest != null && chest.Storage != null)
                {
                    data.hasChest = true;
                    WriteInventory(data.chestSlots, chest.Storage);
                }

                output.Add(data);
            }
        }

        private static void WriteStructureCells(List<SavedStructureCell> output)
        {
            output.Clear();
            var structures = FindFirstObjectByType<StructurePlacementSystem>();
            if (structures == null)
                return;

            var cells = new List<Vector2Int>();
            structures.WriteProcessedCells(cells);
            for (int i = 0; i < cells.Count; i++)
                output.Add(new SavedStructureCell { x = cells[i].x, z = cells[i].y });
        }

        private static void WriteGuardSpawners(List<SavedGuardSpawner> output)
        {
            output.Clear();
            var structures = FindFirstObjectByType<StructurePlacementSystem>();
            if (structures == null)
                return;

            var guards = structures.GuardSpawners;
            for (int i = 0; i < guards.Count; i++)
            {
                if (guards[i] == null || guards[i].Definition == null)
                    continue;

                output.Add(new SavedGuardSpawner
                {
                    creatureId = guards[i].Definition.Id,
                    position = guards[i].transform.position,
                    maxPopulation = guards[i].MaxPopulation,
                    spawnRadius = guards[i].SpawnRadius,
                    spawnOnlyAtNight = guards[i].SpawnOnlyAtNight,
                });
            }
        }

        // ==================================================================
        // LOAD
        // ==================================================================

        /// <summary>Reads the slot and reloads the scene; restoration runs across the scene-load lifecycle (see class summary).</summary>
        public bool Load(string slotName)
        {
            string path = GetSlotPath(slotName);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[Save] Slot '{slotName}' does not exist.");
                return false;
            }

            SaveGame save;
            try
            {
                save = JsonUtility.FromJson<SaveGame>(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Save] Slot '{slotName}' failed to parse: {exception.Message}");
                return false;
            }

            if (save == null)
            {
                Debug.LogError($"[Save] Slot '{slotName}' parsed to nothing — corrupt file.");
                return false;
            }

            if (save.version > SaveGame.CurrentVersion)
            {
                Debug.LogWarning(
                    $"[Save] Slot '{slotName}' is format v{save.version}, this build reads v{SaveGame.CurrentVersion} — " +
                    "loading best-effort (unknown fields are ignored by design).");
            }

            pendingLoad = save;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            return true;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (pendingLoad == null)
                return;

            IsApplyingLoad = true;

            // --- PRE-START pass (Awakes ran; no Start yet) -----------------
            var world = FindFirstObjectByType<VoxelWorld>();
            if (world != null)
            {
                world.ApplyLoadedSeed(pendingLoad.worldSeed);

                foreach (SavedChunkDelta saved in pendingLoad.terrain)
                    world.RegisterPendingChunkDelta(new Vector2Int(saved.coordX, saved.coordZ), ToChunkDelta(saved));
            }
            else
            {
                Debug.LogError("[Save] Loaded scene has no VoxelWorld — terrain not restored.");
            }

            var timeOfDay = FindFirstObjectByType<TimeOfDayController>();
            if (timeOfDay != null)
                timeOfDay.RestoreTime(pendingLoad.timeOfDay01, pendingLoad.dayNumber);

            var structures = FindFirstObjectByType<StructurePlacementSystem>();
            if (structures != null && pendingLoad.structureCells.Count > 0)
            {
                var cells = new List<Vector2Int>(pendingLoad.structureCells.Count);
                foreach (SavedStructureCell cell in pendingLoad.structureCells)
                    cells.Add(new Vector2Int(cell.x, cell.z));
                structures.RestoreProcessedCells(cells);
            }

            StartCoroutine(ApplyAfterStart(pendingLoad));
            pendingLoad = null;
        }

        /// <summary>POST-START pass: waits one frame so every Start (starting items, stat instances) ran, then restores on top.</summary>
        private IEnumerator ApplyAfterStart(SaveGame save)
        {
            yield return null;

            RestorePlayer(save.player);
            RestorePieces(save.pieces);
            RestoreGuardSpawners(save.guardSpawners);

            IsApplyingLoad = false;
            ResetAutosaveClock();
            Debug.Log($"[Save] Load applied: {save.terrain.Count} chunk delta(s) queued, {save.pieces.Count} piece(s) restored.");
        }

        private static ChunkDelta ToChunkDelta(SavedChunkDelta saved)
        {
            var delta = new ChunkDelta
            {
                CellIndices = saved.cellIndices.ToArray(),
                BlockIdTableIndices = saved.blockIdTableIndices.ToArray(),
                BlockIdTable = saved.blockIdTable.ToArray(),
            };

            foreach (SavedPromotedCell promotion in saved.promotions)
            {
                byte[] bits;
                try
                {
                    bits = Convert.FromBase64String(promotion.bitsBase64);
                }
                catch (FormatException)
                {
                    Debug.LogWarning($"[Save] Corrupt sub-voxel payload at cell {promotion.cellIndex} — skipped.");
                    continue;
                }

                delta.Promotions.Add(new PromotedCellDelta
                {
                    CellIndex = promotion.cellIndex,
                    Resolution = promotion.resolution,
                    Bits = bits,
                });
            }

            return delta;
        }

        private static void RestorePlayer(SavedPlayer data)
        {
            var player = FindFirstObjectByType<PlayerReferences>();
            if (player == null)
            {
                Debug.LogError("[Save] No player in the loaded scene — player state not restored.");
                return;
            }

            // Pose (controller must be off across the teleport).
            CharacterController controller = player.Controller;
            bool hadController = controller != null && controller.enabled;
            if (hadController)
                controller.enabled = false;

            player.transform.SetPositionAndRotation(data.position, Quaternion.Euler(0f, data.yaw, 0f));

            if (hadController)
                controller.enabled = true;

            if (player.Locomotion != null)
                player.Locomotion.SetVerticalVelocity(0f);
            if (player.CameraController != null)
                player.CameraController.RestorePitch(data.pitch);

            // Inventory: silent per-slot writes, one notification (which also
            // refreshes carry weight, UI and the equipped item / held model).
            var inventory = player.GetComponent<InventorySystem>();
            if (inventory != null)
                RestoreInventory(inventory, data.inventory);

            var selector = player.GetComponent<HotbarSelector>();
            if (selector != null)
                selector.SelectSlot(data.selectedHotbarIndex);

            // Stats: current values only. Modifiers are deliberately NOT
            // saved — they are all re-derived state (PlayerSurvival re-applies
            // its rules within a tick; equip modifiers re-apply through the
            // equip-changed event the inventory notification just fired).
            var stats = player.GetComponent<StatContainer>();
            if (stats != null)
            {
                foreach (SavedStat stat in data.stats)
                    stats.SetCurrent(stat.statId, stat.current);
            }
        }

        private static void RestoreInventory(InventorySystem inventory, List<SavedItemSlot> slots)
        {
            ItemDatabase items = ItemDatabase.Instance;

            // Clear everything first (Start seeded starting items), then lay
            // the saved slots down index-aligned.
            for (int i = 0; i < inventory.SlotCount; i++)
                inventory.RestoreSlot(i, null, 0, 1f);

            for (int i = 0; i < slots.Count; i++)
            {
                SavedItemSlot slot = slots[i];
                if (string.IsNullOrEmpty(slot.itemId))
                    continue;

                if (items == null || !items.TryGet(slot.itemId, out ItemDefinition item))
                {
                    Debug.LogWarning($"[Save] Item id '{slot.itemId}' no longer exists — slot {i} loads empty.");
                    continue;
                }

                inventory.RestoreSlot(i, item, slot.count, slot.durability01);
            }

            inventory.NotifyExternalRestore();
        }

        private static void RestorePieces(List<SavedPiece> pieces)
        {
            BuildingPieceDatabase database = BuildingPieceDatabase.Instance;
            if (database == null)
                return;

            Transform parent = PlacedPieceRegistry.Instance != null ? PlacedPieceRegistry.Instance.transform : null;

            foreach (SavedPiece saved in pieces)
            {
                if (!database.TryGet(saved.pieceId, out BuildingPieceDefinition definition) || definition.Prefab == null)
                {
                    Debug.LogWarning($"[Save] Piece id '{saved.pieceId}' no longer exists — skipped.");
                    continue;
                }

                GameObject instance = Instantiate(definition.Prefab, saved.position, saved.rotation, parent);
                var piece = instance.GetComponent<BuildingPiece>();
                if (piece == null)
                    piece = instance.AddComponent<BuildingPiece>();

                piece.Initialize(definition);
                piece.RestoreHealth(saved.health);

                if (saved.campfireFuel >= 0f)
                {
                    var campfire = piece.GetComponentInChildren<CampfireBehavior>(true);
                    if (campfire != null)
                        campfire.RestoreSaveState(saved.campfireFuel, saved.campfireLit);
                }

                if (saved.hasDoorState && saved.doorOpen)
                {
                    var door = piece.GetComponentInChildren<DoorBehavior>(true);
                    if (door != null)
                        door.RestoreOpen(true);
                }

                if (saved.hasChest)
                {
                    var chest = piece.GetComponentInChildren<ChestBehavior>(true);
                    if (chest != null && chest.Storage != null)
                        RestoreInventory(chest.Storage, saved.chestSlots);
                }
            }
        }

        private static void RestoreGuardSpawners(List<SavedGuardSpawner> guards)
        {
            if (guards.Count == 0)
                return;

            var structures = FindFirstObjectByType<StructurePlacementSystem>();
            var creatures = Data.Creatures.CreatureDatabase.Instance;
            if (structures == null || creatures == null)
                return;

            foreach (SavedGuardSpawner guard in guards)
            {
                if (!creatures.TryGet(guard.creatureId, out Data.Creatures.CreatureDefinition creature))
                {
                    Debug.LogWarning($"[Save] Guard creature id '{guard.creatureId}' no longer exists — spawner skipped.");
                    continue;
                }

                structures.RestoreGuardSpawner(
                    creature, guard.position, guard.maxPopulation, guard.spawnRadius, guard.spawnOnlyAtNight);
            }
        }

        // ==================================================================
        // Slots on disk
        // ==================================================================

        public static string GetSlotPath(string slotName)
        {
            return Path.Combine(SaveFolder, SanitizeSlotName(slotName) + ".json");
        }

        /// <summary>Slot names become file names — strip anything the filesystem would reject.</summary>
        public static string SanitizeSlotName(string slotName)
        {
            if (string.IsNullOrWhiteSpace(slotName))
                return "slot";

            var builder = new System.Text.StringBuilder(slotName.Length);
            foreach (char c in slotName.Trim())
                builder.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return builder.ToString();
        }

        /// <summary>Existing slots, newest first, with their write timestamps (the menu lists these).</summary>
        public static List<(string slotName, DateTime writtenAt)> EnumerateSlots()
        {
            var slots = new List<(string, DateTime)>();
            if (!Directory.Exists(SaveFolder))
                return slots;

            foreach (string file in Directory.GetFiles(SaveFolder, "*.json"))
                slots.Add((Path.GetFileNameWithoutExtension(file), File.GetLastWriteTime(file)));

            slots.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            return slots;
        }

        public static bool DeleteSlot(string slotName)
        {
            string path = GetSlotPath(slotName);
            if (!File.Exists(path))
                return false;

            File.Delete(path);
            return true;
        }
    }
}
