using IslandGame.Building;
using IslandGame.Data.Building;
using IslandGame.Inventory;
using UnityEngine;

namespace IslandGame.Player
{
    /// <summary>
    /// The default death penalty — Valheim-style "your bag stays where you
    /// fell": every BACKPACK slot moves into a Gravestone spawned at the
    /// death spot; the HOTBAR is kept. Chosen as the default because it
    /// punishes without restarting the player: tools and weapons survive (no
    /// miserable bare-handed rerun), while the heavy bulk — resources, which
    /// the carry-weight system makes the backpack's cargo — must be won back
    /// with a corpse run.
    ///
    /// The gravestone is a REAL placed piece (the chest pattern: BuildingPiece
    /// + ChestBehavior + its own InventorySystem), instantiated through the
    /// same Initialize/registry contract player placement and structures use.
    /// That one decision buys everything downstream for free: looting is the
    /// existing chest transfer interaction, deconstruction works, and the
    /// SAVE SYSTEM already persists registered pieces including chest slots —
    /// no third container mechanism, no new serialization.
    ///
    /// Anything the gravestone can't hold (pathological all-slots-full cases)
    /// spills as WorldItem drops beside it — player property is never
    /// destroyed by this policy.
    /// </summary>
    [CreateAssetMenu(fileName = "DropBackpackDeathPenalty", menuName = "Island Game/Death Penalty/Drop Backpack")]
    public sealed class DropBackpackDeathPenalty : DeathPenaltyPolicy
    {
        [Tooltip("Stable piece ID of the gravestone (created by Island Game/Data/Create Death Content).")]
        [SerializeField] private string gravestonePieceId = "gravestone";

        [Tooltip("Harsher variant: the hotbar drops too (equipped tools included). Off = the Valheim-style default.")]
        [SerializeField] private bool dropHotbarToo;

        private static readonly RaycastHit[] GroundBuffer = new RaycastHit[16];

        public override Transform Apply(GameObject player, Vector3 deathPosition)
        {
            var inventory = player.GetComponent<InventorySystem>();
            if (inventory == null)
                return null;

            // Anything to drop at all? An empty backpack spawns no stone.
            bool anyLoot = false;
            for (int i = 0; i < inventory.SlotCount && !anyLoot; i++)
                anyLoot = ShouldDrop(inventory, i) && !inventory.GetSlot(i).IsEmpty;

            if (!anyLoot)
                return null;

            BuildingPiece gravestone = SpawnGravestone(player, deathPosition);
            if (gravestone == null)
                return null;

            var chest = gravestone.GetComponentInChildren<ChestBehavior>(true);
            InventorySystem storage = chest != null ? chest.Storage : null;

            // Move the flagged slots over: silent slot writes + one notify,
            // the same convention the save system's restore uses. Durability
            // rides along (worn tools come back worn).
            for (int i = 0; i < inventory.SlotCount; i++)
            {
                if (!ShouldDrop(inventory, i))
                    continue;

                InventorySlot slot = inventory.GetSlot(i);
                if (slot.IsEmpty)
                    continue;

                int stored = storage != null ? storage.AddItem(slot.Item, slot.Count, slot.Durability01) : 0;
                int leftover = slot.Count - stored;
                if (leftover > 0)
                {
                    // Never destroy property: overflow scatters beside the stone.
                    WorldItem.Spawn(slot.Item, leftover, slot.Durability01,
                        gravestone.transform.position + Vector3.up * 0.8f,
                        new Vector3(Random.Range(-1f, 1f), 2f, Random.Range(-1f, 1f)));
                }

                inventory.RestoreSlot(i, null, 0, 1f);
            }

            inventory.NotifyExternalRestore();
            return gravestone.transform;
        }

        private bool ShouldDrop(InventorySystem inventory, int slotIndex)
        {
            return dropHotbarToo || !inventory.IsHotbarIndex(slotIndex);
        }

        /// <summary>The exact instantiation contract player placement and structures use: prefab under the registry, Initialize(definition).</summary>
        private BuildingPiece SpawnGravestone(GameObject player, Vector3 deathPosition)
        {
            BuildingPieceDatabase database = BuildingPieceDatabase.Instance;
            if (database == null || !database.TryGet(gravestonePieceId, out BuildingPieceDefinition definition)
                || definition.Prefab == null)
            {
                Debug.LogError(
                    $"[Death] Gravestone piece '{gravestonePieceId}' missing — run Island Game/Data/Create Death Content. " +
                    "No loot was dropped (inventory kept).");
                return null;
            }

            Vector3 position = FindGround(deathPosition, player.transform);
            Quaternion rotation = Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f);

            Transform parent = PlacedPieceRegistry.Instance != null ? PlacedPieceRegistry.Instance.transform : null;
            GameObject instance = Object.Instantiate(definition.Prefab, position, rotation, parent);

            var piece = instance.GetComponent<BuildingPiece>();
            if (piece == null)
                piece = instance.AddComponent<BuildingPiece>();

            piece.Initialize(definition);
            Debug.Log($"[Death] Gravestone placed at ({position.x:0.#}, {position.y:0.#}, {position.z:0.#}).", piece);
            return piece;
        }

        /// <summary>Snaps to the ground under the death spot (skipping the player's own colliders); falls back to the raw position mid-air/water.</summary>
        private static Vector3 FindGround(Vector3 deathPosition, Transform playerRoot)
        {
            Vector3 origin = deathPosition + Vector3.up * 1.5f;
            int hitCount = Physics.RaycastNonAlloc(
                origin, Vector3.down, GroundBuffer, 30f, ~0, QueryTriggerInteraction.Ignore);

            float bestDistance = float.PositiveInfinity;
            Vector3 best = deathPosition;
            for (int i = 0; i < hitCount; i++)
            {
                if (GroundBuffer[i].collider.transform.IsChildOf(playerRoot))
                    continue;

                if (GroundBuffer[i].distance < bestDistance)
                {
                    bestDistance = GroundBuffer[i].distance;
                    best = GroundBuffer[i].point;
                }
            }

            return best;
        }
    }
}
