using System.Collections.Generic;
using IslandGame.Data.Blocks;
using IslandGame.Data.Items;
using IslandGame.Inventory;
using IslandGame.Player;
using UnityEngine;

namespace IslandGame.Terrain
{
    /// <summary>
    /// Camera-raycast mining and placing against the voxel world.
    ///
    /// MINING (hold mine button): progress accumulates while aiming at the
    /// same block and completes after Hardness seconds, scaled by the equipped
    /// tool's MiningSpeedMultiplier when the tool IsEfficientAgainst the block
    /// (Phase 3's split: the list is speed, the tier is permission). TIER
    /// POLICY: a block whose RequiredToolTier exceeds the equipped tier is
    /// BLOCKED outright, not slowed — bare hands (or any non-tool item) are
    /// tier 0, so stone with RequiredToolTier 1 needs a pickaxe, full stop.
    /// Blocked-vs-slow was our call; blocked reads clearest without UI and
    /// matches the survival references. Unbreakable-flagged blocks never mine.
    ///
    /// RADIUS MINING (small-voxel phase): when the equipped tool has a
    /// MiningRadius > 0, a COMPLETED mining hit carves a sub-voxel sphere at
    /// the aim point (biased a quarter-radius into the surface so bites eat
    /// material, not air) instead of deleting the whole block — timing, tier
    /// permission and the crack overlay are the exact same pipeline, only the
    /// geometry outcome changes. The sphere may span several blocks; cells
    /// above the tool's tier, unbreakable, liquid or non-solid are skipped by
    /// the permission filter. Radius 0 (bare hands, unauthored tools) keeps
    /// classic whole-block mining unchanged.
    ///
    /// DROP RULE (deliberate): a block yields its DropItem only when its cell
    /// FULLY empties — partial carving is visual chip-away with the reward on
    /// full clear. Proportional partial drops were rejected: they need
    /// fractional bookkeeping per action, rounding creates grind/dupe
    /// exploits, and total yield should equal classic mining (one block
    /// volume = one drop roll). A single bite that empties several cells
    /// grants each cell's drops — that volume WAS mined.
    ///
    /// Broken blocks grant their DropItem via InventorySystem.AddItem; any
    /// remainder that doesn't fit spawns as a WorldItem at the block.
    ///
    /// PLACING (place button): if the equipped hotbar item is Block/Placeable
    /// with a PlacedBlock, the block goes into the cell on the aimed face —
    /// unless that cell is occupied or overlaps the player — and one unit is
    /// consumed from the equipped stack.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerReferences))]
    public sealed class PlayerBlockInteraction : MonoBehaviour
    {
        [Tooltip("Maximum mine/place distance from the camera, meters.")]
        [SerializeField] private float reach = 4.5f;

        [Tooltip("Wired by the Voxel World builder; auto-resolved when empty.")]
        [SerializeField] private VoxelWorld world;

        private PlayerReferences references;
        private InventorySystem inventory;
        private HotbarSelector selector;
        private MiningDebrisEffect debris;

        private readonly List<VoxelWorld.CarvedBlock> carvedBuffer = new List<VoxelWorld.CarvedBlock>(8);

        private Vector3Int miningCell;
        private bool hasMiningTarget;
        private float miningProgressSeconds;

        /// <summary>0-1 progress on the current mining target (drives BlockTargetIndicator's crack overlay).</summary>
        public float MiningProgress01 { get; private set; }

        /// <summary>True while the crosshair rests on a voxel block within reach — updated every frame, not just while mining.</summary>
        public bool HasAimedBlock { get; private set; }

        /// <summary>The aimed cell (valid while HasAimedBlock).</summary>
        public Vector3Int AimedCell { get; private set; }

        /// <summary>Definition of the aimed block (null while nothing is aimed).</summary>
        public BlockDefinition AimedBlock { get; private set; }

        /// <summary>Exact surface point of the aim ray (valid while HasAimedBlock) — the carve-sphere center.</summary>
        public Vector3 AimedHitPoint { get; private set; }

        /// <summary>Surface normal at AimedHitPoint (valid while HasAimedBlock).</summary>
        public Vector3 AimedHitNormal { get; private set; }

        /// <summary>False when the aimed block is unbreakable or above the equipped tool tier — the indicator shows that as blocked.</summary>
        public bool AimedBlockMinable { get; private set; }

        /// <summary>True while mining progress is accumulating on MiningCell.</summary>
        public bool HasMiningTarget => hasMiningTarget;

        public Vector3Int MiningCell => miningCell;

        private void Awake()
        {
            references = GetComponent<PlayerReferences>();
            inventory = GetComponent<InventorySystem>();
            selector = GetComponent<HotbarSelector>();
        }

        private void Start()
        {
            if (world == null)
                world = FindFirstObjectByType<VoxelWorld>();

            if (world == null)
            {
                Debug.LogError("PlayerBlockInteraction: no VoxelWorld in the scene — run Island Game/World/Create Voxel World.", this);
                enabled = false;
                return;
            }

            debris = MiningDebrisEffect.GetOrCreate();
        }

        private void OnEnable()
        {
            references.InputHandler.PlacePressed += TryPlace;
        }

        private void OnDisable()
        {
            references.InputHandler.PlacePressed -= TryPlace;
        }

        private void Update()
        {
            UpdateAim();

            if (references.InputHandler.MineHeld)
                UpdateMining(Time.deltaTime);
            else
                ResetMining();
        }

        // ------------------------------------------------------------------
        // Aiming (one raycast per frame, shared by the indicator and mining)
        // ------------------------------------------------------------------

        private void UpdateAim()
        {
            HasAimedBlock = false;
            AimedBlock = null;
            AimedBlockMinable = false;

            if (!RaycastVoxels(out RaycastHit hit))
                return;

            // Step half a block INTO the face to land inside the aimed block.
            Vector3Int cell = Vector3Int.FloorToInt(hit.point - hit.normal * 0.5f);
            BlockDefinition block = world.GetBlock(cell);
            if (block == null)
                return;

            HasAimedBlock = true;
            AimedCell = cell;
            AimedBlock = block;
            AimedHitPoint = hit.point;
            AimedHitNormal = hit.normal;

            ItemDefinition equipped = selector != null ? selector.EquippedItem : null;
            int tier = equipped != null && equipped.IsTool ? equipped.ToolTier : 0;
            AimedBlockMinable = !block.HasBehavior(BlockBehaviorFlags.Unbreakable)
                                && block.RequiredToolTier <= tier;
        }

        // ------------------------------------------------------------------
        // Mining
        // ------------------------------------------------------------------

        private void UpdateMining(float deltaTime)
        {
            // Permission (tier/unbreakable) already computed by UpdateAim —
            // blocked outright, see tier policy in the class summary.
            if (!HasAimedBlock || !AimedBlockMinable)
            {
                ResetMining();
                return;
            }

            BlockDefinition block = AimedBlock;
            if (!hasMiningTarget || AimedCell != miningCell)
            {
                miningCell = AimedCell;
                hasMiningTarget = true;
                miningProgressSeconds = 0f;
            }

            ItemDefinition equipped = selector != null ? selector.EquippedItem : null;
            bool usingTool = equipped != null && equipped.IsTool;
            float speed = usingTool && equipped.IsEfficientAgainst(block) ? equipped.MiningSpeedMultiplier : 1f;
            miningProgressSeconds += speed * deltaTime;
            MiningProgress01 = block.Hardness <= 0f ? 1f : Mathf.Clamp01(miningProgressSeconds / block.Hardness);

            if (miningProgressSeconds >= block.Hardness)
            {
                CompleteMiningBite(miningCell, block);
                ResetMining();
            }
        }

        /// <summary>
        /// One completed mining hit: radius-carve when the equipped tool has a
        /// MiningRadius, classic whole-block removal otherwise. Permission was
        /// already enforced for the AIMED block (AimedBlockMinable); the carve
        /// filter re-applies the same rules per overlapped cell, since a
        /// sphere can span block types the tool is not rated for.
        /// </summary>
        private void CompleteMiningBite(Vector3Int cell, BlockDefinition block)
        {
            ItemDefinition equipped = selector != null ? selector.EquippedItem : null;
            float radius = equipped != null && equipped.IsTool ? equipped.MiningRadius : 0f;

            if (radius <= 0f)
            {
                // Classic path — unchanged behavior for radius-less tools/hands.
                if (world.SetBlock(cell, BlockPalette.AirId))
                {
                    GrantDrops(cell, block);
                    debris?.EmitBurst(cell + new Vector3(0.5f, 0.5f, 0.5f), block, 10);
                }

                return;
            }

            // Bias the sphere a quarter-radius into the surface: centered
            // exactly on the face, half the bite would carve air.
            Vector3 center = AimedHitPoint - AimedHitNormal * (radius * 0.25f);
            int tier = equipped.ToolTier;

            carvedBuffer.Clear();
            int cleared = world.CarveSphere(center, radius, def => CanCarve(def, tier), carvedBuffer);

            // Fully-emptied cells pay out exactly like classic mining.
            for (int i = 0; i < carvedBuffer.Count; i++)
                GrantDrops(carvedBuffer[i].Cell, carvedBuffer[i].Definition);

            if (cleared > 0)
                debris?.EmitBurst(AimedHitPoint, block, 12);
        }

        /// <summary>Per-cell carve permission: same tier/unbreakable policy as aiming, plus liquids and non-solids stay untouched by tools.</summary>
        private static bool CanCarve(BlockDefinition definition, int tier)
        {
            return definition != null
                   && definition.IsSolid
                   && !definition.HasBehavior(BlockBehaviorFlags.Unbreakable)
                   && !definition.HasBehavior(BlockBehaviorFlags.Liquid)
                   && definition.RequiredToolTier <= tier;
        }

        /// <summary>The one drop-granting path — classic breaks and fully-carved cells both land here.</summary>
        private void GrantDrops(Vector3Int cell, BlockDefinition block)
        {
            if (block == null || block.DropItem == null)
                return;

            int count = Random.Range(block.DropCountMin, block.DropCountMax + 1);
            if (count <= 0)
                return;

            int added = inventory != null ? inventory.AddItem(block.DropItem, count) : 0;
            int leftover = count - added;
            if (leftover > 0)
            {
                // Inventory full: the drop falls at the mined cell instead of vanishing.
                WorldItem.Spawn(block.DropItem, leftover, 1f,
                    cell + new Vector3(0.5f, 0.5f, 0.5f), Vector3.up * 1.5f);
            }
        }

        private void ResetMining()
        {
            hasMiningTarget = false;
            miningProgressSeconds = 0f;
            MiningProgress01 = 0f;
        }

        // ------------------------------------------------------------------
        // Placing
        // ------------------------------------------------------------------

        private void TryPlace()
        {
            ItemDefinition equipped = selector != null ? selector.EquippedItem : null;
            if (equipped == null || equipped.PlacedBlock == null)
                return;
            if (equipped.Category != ItemCategory.Block && equipped.Category != ItemCategory.Placeable)
                return;

            if (!RaycastVoxels(out RaycastHit hit))
                return;

            // Step half a block OUT of the face to land in the adjacent cell.
            Vector3Int cell = Vector3Int.FloorToInt(hit.point + hit.normal * 0.5f);
            if (cell.y < 0 || cell.y >= Chunk.SizeY)
                return;

            // Air is placeable; so is liquid — building INTO water replaces the
            // water cell (no fluid simulation yet, water is static volume data).
            ushort targetId = world.GetBlockId(cell);
            if (targetId != BlockPalette.AirId)
            {
                BlockDefinition target = world.Palette.GetDefinition(targetId);
                if (target == null || !target.HasBehavior(BlockBehaviorFlags.Liquid))
                    return;
            }

            // Never place a solid block inside the player's own capsule.
            var cellBounds = new Bounds(cell + new Vector3(0.5f, 0.5f, 0.5f), Vector3.one);
            if (equipped.PlacedBlock.IsSolid && references.Controller.bounds.Intersects(cellBounds))
                return;

            ushort blockId = world.Palette.GetId(equipped.PlacedBlock);
            if (blockId == BlockPalette.AirId)
                return;

            if (world.SetBlock(cell, blockId))
                inventory.ConsumeFromSlot(selector.SelectedIndex, 1);
        }

        // ------------------------------------------------------------------
        // Shared
        // ------------------------------------------------------------------

        private readonly RaycastHit[] raycastBuffer = new RaycastHit[16];

        private bool RaycastVoxels(out RaycastHit hit)
        {
            // Shared skip-own-rig aim ray (PlayerAimRaycast, extracted in the
            // building phase — the building systems cast the same ray).
            if (!PlayerAimRaycast.Raycast(references.CameraPivot, transform, reach, raycastBuffer, out hit))
                return false;

            // Only voxel geometry counts; a world item or prop in the way
            // simply blocks interaction rather than mining what's behind it.
            return hit.collider.GetComponentInParent<ChunkView>() != null;
        }
    }
}
