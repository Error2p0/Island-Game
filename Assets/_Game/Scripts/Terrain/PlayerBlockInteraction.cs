using System.Collections.Generic;
using IslandGame.Data.Blocks;
using IslandGame.Data.Items;
using IslandGame.Data.Stats;
using IslandGame.Inventory;
using IslandGame.Player;
using IslandGame.Stats;
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
    /// RADIUS MINING (small-voxel phase): when the effective MiningProfile has
    /// a Radius > 0, a COMPLETED mining hit carves a sub-voxel sphere at the
    /// aim point (biased a quarter-radius into the surface so bites eat
    /// material, not air) instead of deleting the whole block — timing, tier
    /// permission and the crack overlay are the exact same pipeline, only the
    /// geometry outcome changes. The sphere may span several blocks; cells
    /// above the profile's tier, unbreakable, liquid or non-solid are skipped
    /// by the permission filter. Radius ≤ 0 (radius-less authored tools)
    /// keeps classic whole-block mining unchanged.
    ///
    /// MINING PROFILE (organic-terrain phase 3): every tier/speed/radius read
    /// goes through MiningProfile.Resolve(equipped) — including the implicit
    /// BARE HANDS profile (tier 0, small radius, slow) when nothing tool-like
    /// is equipped, so hands dig soft ground organically through the exact
    /// same code path tools use. MiningRadiusIndicator previews the same
    /// profile, which is what makes the highlight trustworthy.
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
    ///
    /// RADIUS PLACEMENT: a block item with PlaceRadius > 0 instead FILLS a
    /// sub-voxel sphere at the hit point (biased a quarter-radius OUT of the
    /// surface — CarveCenter's mirror) through VoxelWorld.FillSphere: air and
    /// liquid cells fill, same-id partials top up (demoting back to plain
    /// blocks at 100%), other solids are untouched, and cells overlapping the
    /// player's capsule are skipped. COST is 1 item per full block volume
    /// actually added, rounded up, all-or-nothing — the mirror of mining's
    /// one-block-volume = one-drop economy. MiningRadiusIndicator previews
    /// the identical fill query.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerReferences))]
    public sealed class PlayerBlockInteraction : MonoBehaviour
    {
        [Tooltip("Maximum mine/place distance from the camera, meters.")]
        [SerializeField] private float reach = 4.5f;

        // How far a hit point steps through/off the hit face to identify the
        // cell: big enough to clear float error on the face plane, smaller
        // than the smallest sub-cell (1/16 block), so sub-voxel-shaped
        // surfaces resolve to the block the player is actually looking at.
        private const float FaceStepIn = 0.05f;

        [Tooltip("Wired by the Voxel World builder; auto-resolved when empty.")]
        [SerializeField] private VoxelWorld world;

        private PlayerReferences references;
        private InventorySystem inventory;
        private HotbarSelector selector;
        private StatContainer statContainer;
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

        /// <summary>
        /// The effective mining parameters this frame (equipped tool or the
        /// bare-hands fallback) — the SAME values the next completed bite
        /// executes with. MiningRadiusIndicator renders exactly this.
        /// </summary>
        public MiningProfile ActiveProfile { get; private set; } = MiningProfile.Resolve(null);

        private void Awake()
        {
            references = GetComponent<PlayerReferences>();
            inventory = GetComponent<InventorySystem>();
            selector = GetComponent<HotbarSelector>();
            statContainer = GetComponent<StatContainer>();
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

            // Refresh the profile every frame regardless of aim — the same
            // per-frame equipped read as before, now through the one resolver.
            ActiveProfile = MiningProfile.Resolve(selector != null ? selector.EquippedItem : null);

            if (!RaycastVoxels(out RaycastHit hit))
                return;

            // Step slightly INTO the face to land inside the aimed block.
            // Half a block worked when every face lay on the block grid, but
            // organic/carved surfaces put sub-voxel faces anywhere inside a
            // cell — a half-block step through a thin partial shell selects
            // the block BEHIND the one the player actually sees (wrong tier
            // check, wrong block mined). FaceStepIn clears face-plane float
            // error while staying below one sub-cell (1/16 block = 0.0625).
            Vector3Int cell = Vector3Int.FloorToInt(hit.point - hit.normal * FaceStepIn);
            BlockDefinition block = world.GetBlock(cell);
            if (block == null)
                return;

            HasAimedBlock = true;
            AimedCell = cell;
            AimedBlock = block;
            AimedHitPoint = hit.point;
            AimedHitNormal = hit.normal;

            AimedBlockMinable = ActiveProfile.CanMine(block);
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

            // Profile rate: authored efficiency for tools, the slow bare-hands
            // rate otherwise — one rule, shared with the preview's state.
            float speed = ActiveProfile.SpeedAgainst(block);

            // Stats phase: the mining_speed stat multiplies ON TOP of the
            // tool's authored efficiency, so equip modifiers and future buffs
            // (food, potions) genuinely mine faster. 1 when the stat is absent.
            if (statContainer != null)
                speed *= Mathf.Max(0f, statContainer.GetValue(StatIds.MiningSpeed, 1f));

            miningProgressSeconds += speed * deltaTime;
            MiningProgress01 = block.Hardness <= 0f ? 1f : Mathf.Clamp01(miningProgressSeconds / block.Hardness);

            if (miningProgressSeconds >= block.Hardness)
            {
                CompleteMiningBite(miningCell, block);
                ResetMining();
            }
        }

        /// <summary>
        /// One completed mining hit: radius-carve when the effective profile
        /// has a radius (tools and bare hands alike), classic whole-block
        /// removal otherwise. Permission was already enforced for the AIMED
        /// block (AimedBlockMinable); the carve filter re-applies the same
        /// rules per overlapped cell, since a sphere can span block types the
        /// profile is not rated for. Radius, center bias and filter all come
        /// from ActiveProfile — identical to what the indicator previewed.
        /// </summary>
        private void CompleteMiningBite(Vector3Int cell, BlockDefinition block)
        {
            ItemDefinition equipped = selector != null ? selector.EquippedItem : null;
            MiningProfile profile = ActiveProfile;

            if (profile.Radius <= 0f)
            {
                // Classic path — unchanged behavior for radius-less tools.
                if (world.SetBlock(cell, BlockPalette.AirId))
                {
                    GrantDrops(cell, block);
                    debris?.EmitBurst(cell + new Vector3(0.5f, 0.5f, 0.5f), block, 10);
                    ApplyToolWear(equipped);
                }

                return;
            }

            Vector3 center = profile.CarveCenter(AimedHitPoint, AimedHitNormal);

            carvedBuffer.Clear();
            int cleared = world.CarveSphere(center, profile.Radius, profile.CanCarve, carvedBuffer);

            // Fully-emptied cells pay out exactly like classic mining.
            for (int i = 0; i < carvedBuffer.Count; i++)
                GrantDrops(carvedBuffer[i].Cell, carvedBuffer[i].Definition);

            if (cleared > 0)
                debris?.EmitBurst(AimedHitPoint, block, 12);

            // Every completed radius bite wears the tool — it struck rock even
            // when the sphere's fully-cleared-cell payout happens to be zero.
            ApplyToolWear(equipped);
        }

        /// <summary>
        /// Durability phase: one completed mining hit costs the equipped
        /// tool its authored wear. At zero the inventory applies the break
        /// behavior; the equipped item changes (gone or Broken Variant), so
        /// next frame's aim/tier checks naturally re-gate mining — no special
        /// "is broken" state exists anywhere.
        /// </summary>
        private void ApplyToolWear(ItemDefinition equipped)
        {
            if (equipped == null || !equipped.HasDurability || inventory == null || selector == null)
                return;

            inventory.ApplyDurabilityDamage(selector.SelectedIndex, equipped.DurabilityPerMiningHit);
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

            ushort placedId = world.Palette.GetId(equipped.PlacedBlock);
            if (placedId == BlockPalette.AirId)
                return;

            // RADIUS PLACEMENT (the fill mirror of radius mining): a block
            // item with PlaceRadius > 0 fills a sub-voxel sphere at the hit
            // point instead of one cell. COST: 1 item per full block volume
            // of material actually added, rounded up — the mirror of mining's
            // one-block-volume = one-drop economy — and all-or-nothing: the
            // whole previewed fill must be affordable or nothing places.
            if (equipped.PlaceRadius > 0f)
            {
                TryRadiusPlace(equipped, placedId, hit);
                return;
            }

            // Step slightly OUT of the face (see FaceStepIn in UpdateAim):
            // grid-aligned faces land in the adjacent cell exactly as before,
            // while an interior sub-voxel face (carved crater wall) lands in
            // its own still-occupied cell and is correctly rejected below —
            // the old half-block step would have placed a floating block one
            // cell out from the visible surface.
            Vector3Int cell = Vector3Int.FloorToInt(hit.point + hit.normal * FaceStepIn);
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

            if (world.SetBlock(cell, placedId))
                inventory.ConsumeFromSlot(selector.SelectedIndex, 1);
        }

        /// <summary>
        /// One radius placement: price the previewed fill, place only when
        /// the whole volume is affordable, then consume for what was actually
        /// added. Center bias, eligibility and the player-capsule exclusion
        /// all live in VoxelWorld's fill pair — identical to what the
        /// indicator previews.
        /// </summary>
        private void TryRadiusPlace(ItemDefinition equipped, ushort placedId, in RaycastHit hit)
        {
            float radius = equipped.PlaceRadius;
            Vector3 center = VoxelWorld.FillSphereCenter(hit.point, hit.normal, radius);
            Bounds? exclude = equipped.PlacedBlock.IsSolid ? references.Controller.bounds : (Bounds?)null;

            int prospective = world.CollectFillPreview(center, radius, placedId, exclude, null);
            if (prospective == 0)
                return;

            int perBlock = world.SubVoxelResolution * world.SubVoxelResolution * world.SubVoxelResolution;
            int cost = (prospective + perBlock - 1) / perBlock;
            if (inventory == null || inventory.GetItemCount(equipped) < cost)
                return;

            int filled = world.FillSphere(center, radius, placedId, exclude);
            if (filled > 0)
                inventory.RemoveItem(equipped, (filled + perBlock - 1) / perBlock);
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
