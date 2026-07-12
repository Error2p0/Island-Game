using System.Collections.Generic;
using IslandGame.Building;
using IslandGame.Creatures;
using IslandGame.Data.Creatures;
using IslandGame.Data.World;
using IslandGame.Player;
using UnityEngine;

namespace IslandGame.Terrain
{
    /// <summary>
    /// Scatters StructureTemplates across the generated islands — the same
    /// CATEGORY of system as tree scattering (deterministic seeded anchor
    /// cells validated against the generator's height data; the noise/island
    /// shaping itself is untouched), but structures are made of scene
    /// GameObjects, not blocks, so placement runs here as the player
    /// approaches instead of inside per-chunk data generation:
    ///
    ///   SITES — the world is divided into Cell Size anchor cells; each rolls
    ///   seeded occupancy, a jittered position, template choice and yaw —
    ///   all through the generator's own hash, so a seed always produces the
    ///   same ruins in the same places. One structure max per cell = minimum
    ///   spacing is built into the grid.
    ///
    ///   VALIDATION — footprint heights sampled from the generator: Inland
    ///   needs flat grass (above the beach band, bounded variance); Coast
    ///   needs a shore column with open water at the template's seaward
    ///   extent, and the yaw is chosen to POINT seaward.
    ///
    ///   PLACEMENT — pieces instantiate as REAL BuildingPiece prefabs,
    ///   initialized through the same Initialize path player placement uses
    ///   and registered in PlacedPieceRegistry — so deconstruction, weapon
    ///   damage/durability and (Phase 6) saving treat ruins exactly like
    ///   player builds. Chests fill from their loot tables (seeded rolls);
    ///   guard entries configure ordinary CreatureSpawners.
    ///
    ///   Cells process ONCE per session (placed or rejected) and structures
    ///   persist as scene objects — despawning them would discard the
    ///   player's looting/deconstruction. Phase 6 saves placed pieces plus
    ///   this processed-cell set so revisits don't re-spawn looted ruins.
    ///
    /// PERFORMANCE: a 1 s tick scans only unprocessed cells inside Place
    /// Radius (a handful); validating one site costs ~13 height samples of
    /// pure noise math. Placement itself is a few prefab instantiations, once
    /// ever per site.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StructurePlacementSystem : MonoBehaviour
    {
        [Tooltip("Anchor cell size, meters — also the guaranteed minimum spacing floor between structures (one per cell, jittered within the center half).")]
        [Range(24, 128)]
        [SerializeField] private int cellSize = 48;

        [Tooltip("Seeded chance an anchor cell attempts a structure (validation may still reject the site).")]
        [Range(0f, 1f)]
        [SerializeField] private float structureDensity = 0.45f;

        [Tooltip("Cells are evaluated/placed when the player is within this range, meters. Keep inside the voxel data ring.")]
        [Min(40f)]
        [SerializeField] private float placeRadius = 80f;

        [Tooltip("No structures within this distance of the world origin — the spawn beach stays clear.")]
        [Min(0f)]
        [SerializeField] private float minDistanceFromOrigin = 24f;

        [Tooltip("Wired by the builder; auto-resolved when empty.")]
        [SerializeField] private VoxelWorld world;

        private const float TickInterval = 1f;
        private const int FootprintSamples = 8;

        private readonly HashSet<Vector2Int> processedCells = new HashSet<Vector2Int>();
        private readonly List<StructureTemplate> eligibleBuffer = new List<StructureTemplate>(8);

        private PlayerReferences player;
        private float nextTickTime;

        private void Start()
        {
            if (world == null)
                world = FindFirstObjectByType<VoxelWorld>();
        }

        private void Update()
        {
            if (Time.time < nextTickTime)
                return;

            nextTickTime = Time.time + TickInterval;
            Tick();
        }

        private void Tick()
        {
            if (world == null || !world.IsReady)
                return;

            IslandWorldGenerator generator = world.ActiveIslandGenerator;
            if (generator == null)
                return; // debug-flat mode: no islands, no ruins

            StructureTemplateDatabase database = StructureTemplateDatabase.Instance;
            if (database == null || database.Count == 0)
                return;

            if (player == null)
            {
                player = FindFirstObjectByType<PlayerReferences>();
                if (player == null)
                    return;
            }

            Vector3 playerPosition = player.transform.position;
            int minCellX = Mathf.FloorToInt((playerPosition.x - placeRadius) / cellSize);
            int maxCellX = Mathf.FloorToInt((playerPosition.x + placeRadius) / cellSize);
            int minCellZ = Mathf.FloorToInt((playerPosition.z - placeRadius) / cellSize);
            int maxCellZ = Mathf.FloorToInt((playerPosition.z + placeRadius) / cellSize);

            for (int cellZ = minCellZ; cellZ <= maxCellZ; cellZ++)
            {
                for (int cellX = minCellX; cellX <= maxCellX; cellX++)
                {
                    var cell = new Vector2Int(cellX, cellZ);
                    if (processedCells.Contains(cell))
                        continue;

                    ProcessCell(cell, generator, database);
                    processedCells.Add(cell);
                }
            }
        }

        // ------------------------------------------------------------------
        // Site selection (pure functions of world seed + cell coordinates)
        // ------------------------------------------------------------------

        private void ProcessCell(Vector2Int cell, IslandWorldGenerator generator, StructureTemplateDatabase database)
        {
            if (generator.SampleHash01(cell.x, cell.y, 200) >= structureDensity)
                return;

            // Jitter inside the center half of the cell: neighbors can never
            // get closer than half a cell — the spacing guarantee.
            float siteX = (cell.x + 0.25f + generator.SampleHash01(cell.x, cell.y, 201) * 0.5f) * cellSize;
            float siteZ = (cell.y + 0.25f + generator.SampleHash01(cell.x, cell.y, 202) * 0.5f) * cellSize;

            if (siteX * siteX + siteZ * siteZ < minDistanceFromOrigin * minDistanceFromOrigin)
                return;

            // Every template that fits this ground participates in a weighted
            // pick; the winner's yaw/origin are re-derived (validation is a
            // cheap pure function — recomputing beats caching per template).
            eligibleBuffer.Clear();
            float weightTotal = 0f;

            foreach (StructureTemplate template in database.All)
            {
                if (template == null || template.Pieces.Count == 0 || template.SpawnWeight <= 0f)
                    continue;

                if (!TryValidateSite(template, generator, siteX, siteZ, cell, out _, out _))
                    continue;

                eligibleBuffer.Add(template);
                weightTotal += template.SpawnWeight;
            }

            if (eligibleBuffer.Count == 0)
                return;

            float roll = generator.SampleHash01(cell.x, cell.y, 203) * weightTotal;
            StructureTemplate chosen = eligibleBuffer[eligibleBuffer.Count - 1];
            for (int i = 0; i < eligibleBuffer.Count; i++)
            {
                roll -= eligibleBuffer[i].SpawnWeight;
                if (roll <= 0f)
                {
                    chosen = eligibleBuffer[i];
                    break;
                }
            }

            TryValidateSite(chosen, generator, siteX, siteZ, cell, out float chosenYaw, out float chosenOriginY);
            BuildStructure(chosen, new Vector3(siteX, chosenOriginY, siteZ), chosenYaw, cell, generator);
        }

        /// <summary>
        /// Validates the template's footprint at the site against generator
        /// heights. Inland: all grass, bounded variance, origin on the highest
        /// sample. Coast: shore column at the center, open water at the
        /// seaward extent along the returned yaw, origin just above sea level.
        /// </summary>
        private bool TryValidateSite(
            StructureTemplate template, IslandWorldGenerator generator,
            float siteX, float siteZ, Vector2Int cell, out float yaw, out float originY)
        {
            yaw = 0f;
            originY = 0f;

            int seaLevel = generator.SeaLevelY;
            int grassMin = seaLevel + generator.BeachBandSize + 1;
            int centerHeight = generator.SampleHeight(siteX, siteZ);

            if (template.Surface == StructureSurface.Inland)
            {
                if (centerHeight < grassMin)
                    return false;

                int minHeight = centerHeight;
                int maxHeight = centerHeight;
                for (int i = 0; i < FootprintSamples; i++)
                {
                    float angle = i * (Mathf.PI * 2f / FootprintSamples);
                    int height = generator.SampleHeight(
                        siteX + Mathf.Cos(angle) * template.FootprintRadius,
                        siteZ + Mathf.Sin(angle) * template.FootprintRadius);

                    if (height < grassMin)
                        return false; // water/beach inside the footprint

                    minHeight = Mathf.Min(minHeight, height);
                    maxHeight = Mathf.Max(maxHeight, height);
                }

                if (maxHeight - minHeight > template.MaxHeightVariance)
                    return false; // too steep

                yaw = Mathf.Floor(generator.SampleHash01(cell.x, cell.y, 204) * 4f) * 90f;
                originY = maxHeight; // highest ground so no floor sits buried
                return true;
            }

            // Coast: the center column must be shore (sand band around sea
            // level), and one cardinal direction must reach open water at the
            // template's seaward extent — that direction becomes local +Z.
            if (centerHeight < seaLevel - 1 || centerHeight > seaLevel + generator.BeachBandSize + 1)
                return false;

            for (int i = 0; i < 4; i++)
            {
                float candidateYaw = i * 90f;
                Vector3 seaward = Quaternion.Euler(0f, candidateYaw, 0f) * Vector3.forward;
                int farHeight = generator.SampleHeight(
                    siteX + seaward.x * template.CoastSeawardExtent,
                    siteZ + seaward.z * template.CoastSeawardExtent);
                int midHeight = generator.SampleHeight(
                    siteX + seaward.x * template.CoastSeawardExtent * 0.5f,
                    siteZ + seaward.z * template.CoastSeawardExtent * 0.5f);

                if (farHeight <= seaLevel - 2 && midHeight <= seaLevel)
                {
                    yaw = candidateYaw;
                    originY = seaLevel + template.CoastOriginAboveSea;
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------
        // Building (real placed pieces — the player-placement code path)
        // ------------------------------------------------------------------

        private void BuildStructure(
            StructureTemplate template, Vector3 origin, float yaw, Vector2Int cell, IslandWorldGenerator generator)
        {
            Quaternion yawRotation = Quaternion.Euler(0f, yaw, 0f);
            int placedCount = 0;

            var pieceList = template.Pieces;
            for (int i = 0; i < pieceList.Count; i++)
            {
                StructurePieceEntry entry = pieceList[i];
                if (entry?.piece == null || entry.piece.HasDanglingReferences)
                    continue;

                // The ruin roll: same layout, different gaps per site.
                if (entry.omitChance > 0f && generator.SampleHash01(cell.x, cell.y, 300 + i) < entry.omitChance)
                    continue;

                InstantiatePiece(entry.piece, origin + yawRotation * entry.localPosition,
                    yawRotation * Quaternion.Euler(0f, entry.localYawDegrees, 0f));
                placedCount++;
            }

            var chestList = template.Chests;
            for (int i = 0; i < chestList.Count; i++)
            {
                StructureChestEntry entry = chestList[i];
                if (entry?.chestPiece == null || entry.chestPiece.HasDanglingReferences)
                    continue;

                BuildingPiece chestPiece = InstantiatePiece(
                    entry.chestPiece, origin + yawRotation * entry.localPosition,
                    yawRotation * Quaternion.Euler(0f, entry.localYawDegrees, 0f));
                placedCount++;

                FillChest(chestPiece, entry, cell, i, generator);
            }

            var spawnerList = template.Spawners;
            for (int i = 0; i < spawnerList.Count; i++)
            {
                StructureSpawnerEntry entry = spawnerList[i];
                if (entry?.creature == null)
                    continue;

                var spawnerObject = new GameObject($"StructureGuards_{entry.creature.Id}");
                spawnerObject.transform.SetParent(transform, false);
                spawnerObject.transform.position = origin + yawRotation * entry.localOffset;
                spawnerObject.AddComponent<CreatureSpawner>()
                    .Configure(entry.creature, entry.maxPopulation, entry.spawnRadius, entry.spawnOnlyAtNight);
            }

            Debug.Log(
                $"[Structures] Placed '{template.DisplayName}' ({placedCount} piece(s), yaw {yaw:0}°) at " +
                $"({origin.x:0}, {origin.y:0}, {origin.z:0}).");
        }

        /// <summary>The exact instantiation contract player placement uses: prefab under the registry, Initialize(definition) → health, registry, functional Init.</summary>
        private static BuildingPiece InstantiatePiece(
            Data.Building.BuildingPieceDefinition definition, Vector3 position, Quaternion rotation)
        {
            Transform parent = PlacedPieceRegistry.Instance != null ? PlacedPieceRegistry.Instance.transform : null;
            GameObject instance = Instantiate(definition.Prefab, position, rotation, parent);

            var piece = instance.GetComponent<BuildingPiece>();
            if (piece == null)
            {
                Debug.LogError($"[Structures] Piece prefab '{definition.Prefab.name}' has no BuildingPiece on its root.");
                piece = instance.AddComponent<BuildingPiece>();
            }

            piece.Initialize(definition);
            return piece;
        }

        /// <summary>Seeded loot fill: same LootTableEntry semantics as creature drops (chance per line, then count range), straight into the chest's storage.</summary>
        private static void FillChest(
            BuildingPiece chestPiece, StructureChestEntry entry, Vector2Int cell, int chestIndex,
            IslandWorldGenerator generator)
        {
            var chest = chestPiece.GetComponentInChildren<ChestBehavior>(true);
            if (chest == null || chest.Storage == null)
            {
                Debug.LogWarning(
                    $"[Structures] Chest entry {chestIndex} placed '{chestPiece.PieceId}', which has no ChestBehavior — no loot filled.",
                    chestPiece);
                return;
            }

            // Deterministic per (seed, cell, chest): the same world always
            // hides the same treasure.
            int lootSeed = Mathf.RoundToInt(generator.SampleHash01(cell.x, cell.y, 400 + chestIndex) * int.MaxValue);
            var random = new System.Random(lootSeed);

            for (int i = 0; i < entry.loot.Count; i++)
            {
                LootTableEntry line = entry.loot[i];
                if (line?.item == null || random.NextDouble() > line.dropChance)
                    continue;

                int count = random.Next(line.countMin, line.countMax + 1);
                if (count > 0)
                    chest.Storage.AddItem(line.item, count);
            }
        }
    }
}
