using System.Collections.Generic;
using IslandGame.Data.Blocks;
using IslandGame.Data.Building;
using UnityEngine;

namespace IslandGame.Data.Items
{
    /// <summary>
    /// Authored data for one item type. Pure data — no behavior; runtime systems
    /// (inventory, held items, mining, crafting) read these fields and act on
    /// them. Created via the asset menu now, and via the Item Editor window from
    /// Phase 3 on. Registered in the ItemDatabase automatically on import.
    ///
    /// The weapon/tool/holding sections are authored via the Item Editor
    /// window (Phase 3); combat, mining and equipping runtime logic arrive in
    /// later phases and only READ these fields.
    /// </summary>
    [CreateAssetMenu(fileName = "NewItem", menuName = "Island Game/Item Definition")]
    public sealed class ItemDefinition : ScriptableObject, IDefinition
    {
        [Header("Identity")]
        [Tooltip("Stable unique ID (lowercase_underscore). Serialized into inventory saves and recipes — NEVER change it after content has been saved with it. Auto-filled from the asset name when empty.")]
        [SerializeField] private string id;

        [Tooltip("Name shown in inventory, crafting and creative menus.")]
        [SerializeField] private string displayName;

        [TextArea(2, 4)]
        [SerializeField] private string description;

        [Tooltip("Inventory/crafting icon.")]
        [SerializeField] private Sprite icon;

        [SerializeField] private ItemCategory category = ItemCategory.Resource;

        [Header("Stacking & Weight")]
        [Tooltip("Maximum units per inventory slot. 1 = unstackable (tools, weapons).")]
        [Min(1)]
        [SerializeField] private int maxStackSize = 20;

        [Tooltip("Kilograms per unit. The inventory sums (WeightKg × count) over all slots and pushes the normalized result to PlayerLocomotion.SetCarryWeight via CarryLoad — see that class for the convention.")]
        [Min(0f)]
        [SerializeField] private float weightKg = 1f;

        [Header("World Model")]
        [Tooltip("Prefab instantiated when the item is held in hand or dropped into the world. Null = cannot be dropped or shown in hand (abstract resources).")]
        [SerializeField] private GameObject worldModelPrefab;

        [Header("Weapon (authored via the Item Editor from Phase 3)")]
        [Tooltip("True if this item can be swung/used as a weapon.")]
        [SerializeField] private bool isWeapon;

        [Tooltip("Damage per hit. Only read when Is Weapon is true.")]
        [Min(0f)]
        [SerializeField] private float weaponDamage;

        [Tooltip("How this weapon's hits are classified for armor/resistances and hit feedback.")]
        [SerializeField] private DamageType damageType = DamageType.Blunt;

        [Tooltip("Attacks per second when swinging continuously.")]
        [Min(0f)]
        [SerializeField] private float attacksPerSecond = 1f;

        [Tooltip("Hit reach in meters from the camera.")]
        [Min(0f)]
        [SerializeField] private float attackRange = 2f;

        [Tooltip("Swing animation for the upper-body layer. Reference only — animator wiring happens in the held-item phase.")]
        [SerializeField] private AnimationClip attackClip;

        [Header("Tool (authored via the Item Editor from Phase 3)")]
        [Tooltip("True if this item can mine/harvest blocks.")]
        [SerializeField] private bool isTool;

        [Tooltip("What kind of tool this is. Pairs with Efficient Blocks to describe what it's good at.")]
        [SerializeField] private ToolType toolType = ToolType.None;

        [Tooltip("Blocks with Required Tool Tier above this cannot be mined with this tool. Tier 0 = bare hands.")]
        [Min(0)]
        [SerializeField] private int toolTier;

        [Tooltip("Multiplier on mining speed against blocks in Efficient Blocks. Only read when Is Tool is true.")]
        [Min(0f)]
        [SerializeField] private float miningSpeedMultiplier = 1f;

        [Tooltip("World-space radius (meters) of the sub-voxel sphere carved per completed mining hit. 0 = classic behavior: the whole targeted block breaks at once. ~0.55 clears a block in a few satisfying bites.")]
        [Min(0f)]
        [SerializeField] private float miningRadius;

        [Tooltip("Blocks this tool mines at full Mining Speed Multiplier; blocks outside the list (but within tier) mine at bare-hand speed. PERMISSION is always the tier check, this list is only the efficiency bonus.")]
        [SerializeField] private List<BlockDefinition> efficientBlocks = new List<BlockDefinition>();

        [Header("Holding (read by the held-item phase — see HoldSocketConvention)")]
        [SerializeField] private HoldType holdType = HoldType.None;

        [SerializeField] private HoldSocket holdSocket = HoldSocket.RightHand;

        [Tooltip("Item-local position under the hold socket transform when equipped.")]
        [SerializeField] private Vector3 holdLocalPosition = Vector3.zero;

        [Tooltip("Item-local euler rotation under the hold socket transform when equipped.")]
        [SerializeField] private Vector3 holdLocalRotationEuler = Vector3.zero;

        [Header("Block Placement")]
        [Tooltip("For Block/Placeable items: the voxel block placed in the terrain when this item is used. This is the item→block half of the link; BlockDefinition.DropItem is the block→item half.")]
        [SerializeField] private BlockDefinition placedBlock;

        [Header("Building Placement")]
        [Tooltip("For Placeable items: the building piece this item lets the player place (ghost preview + snapping while equipped). Same item→placed convention as Placed Block — set one or the other, never both.")]
        [SerializeField] private BuildingPieceDefinition placedPiece;

        public string Id => id;
        public string DisplayName => displayName;
        public string Description => description;
        public Sprite Icon => icon;
        public ItemCategory Category => category;
        public int MaxStackSize => maxStackSize;

        /// <summary>Kilograms per unit — feed sums through CarryLoad.ToNormalized into PlayerLocomotion.SetCarryWeight.</summary>
        public float WeightKg => weightKg;

        public GameObject WorldModelPrefab => worldModelPrefab;

        public bool IsWeapon => isWeapon;
        public float WeaponDamage => weaponDamage;
        public DamageType DamageType => damageType;
        public float AttacksPerSecond => attacksPerSecond;
        public float AttackRange => attackRange;
        public AnimationClip AttackClip => attackClip;

        public bool IsTool => isTool;
        public ToolType ToolType => toolType;
        public int ToolTier => toolTier;
        public float MiningSpeedMultiplier => miningSpeedMultiplier;

        /// <summary>Carve-sphere radius per completed mining hit; 0 = whole-block classic mining.</summary>
        public float MiningRadius => miningRadius;

        public IReadOnlyList<BlockDefinition> EfficientBlocks => efficientBlocks;

        /// <summary>True when this tool gets its Mining Speed Multiplier against the block (the tier check is separate).</summary>
        public bool IsEfficientAgainst(BlockDefinition block)
        {
            return block != null && efficientBlocks.Contains(block);
        }

        public HoldType HoldType => holdType;
        public HoldSocket HoldSocket => holdSocket;
        public Vector3 HoldLocalPosition => holdLocalPosition;
        public Vector3 HoldLocalRotationEuler => holdLocalRotationEuler;
        public Quaternion HoldLocalRotation => Quaternion.Euler(holdLocalRotationEuler);

        public BlockDefinition PlacedBlock => placedBlock;
        public BuildingPieceDefinition PlacedPiece => placedPiece;

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Convenience only: a fresh asset inherits its name as ID/display name.
            // An existing ID is never regenerated — stability beats tidiness.
            if (string.IsNullOrWhiteSpace(id) && !string.IsNullOrEmpty(name))
                id = name.Trim().ToLowerInvariant().Replace(' ', '_');
            else if (id != null)
                id = id.Trim();

            if (string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrEmpty(name))
                displayName = name;
        }
#endif
    }
}
