using System.Collections.Generic;
using IslandGame.Data.Blocks;
using IslandGame.Data.Building;
using IslandGame.Data.Stats;
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

        [Tooltip("True: the use button FIRES A PROJECTILE (bow-style) instead of the melee sphere-cast. Damage/type/cadence come from the fields above.")]
        [SerializeField] private bool isRangedWeapon;

        [Tooltip("Projectile launch speed, m/s. Only read when Is Ranged Weapon is true.")]
        [Min(1f)]
        [SerializeField] private float projectileSpeed = 30f;

        [Tooltip("Item consumed from the inventory per shot (arrows). Null = fires for free.")]
        [SerializeField] private ItemDefinition ammoItem;

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

        [Header("Durability (tools & weapons; authored via the Item Editor)")]
        [Tooltip("Durability points before breaking. 0 = never degrades (default — resources and pre-durability content are unaffected). Only read for items flagged Tool and/or Weapon.")]
        [Min(0f)]
        [SerializeField] private float maxDurability;

        [Tooltip("Durability points lost per COMPLETED mining hit (a finished bite/block break — misses and aim time cost nothing).")]
        [Min(0f)]
        [SerializeField] private float durabilityPerMiningHit = 1f;

        [Tooltip("Durability points lost per successful weapon use (melee hit that connects with a damageable, or one arrow fired).")]
        [Min(0f)]
        [SerializeField] private float durabilityPerAttackHit = 1f;

        [Tooltip("What happens at 0 durability: Destroy removes the item; DowngradeToBrokenVariant swaps it for the Broken Variant item below.")]
        [SerializeField] private ItemBreakBehavior breakBehavior = ItemBreakBehavior.Destroy;

        [Tooltip("The weaker item this becomes when it breaks (only read for DowngradeToBrokenVariant). Author it as a normal item with worse stats; leave its own Max Durability at 0 so it can't break further, or give it a small pool for a second break.")]
        [SerializeField] private ItemDefinition brokenVariant;

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

        [Header("Consumable")]
        [Tooltip("Hunger restored when eaten (category Consumable; the use/place button eats one unit — see PlayerStats). 0 = not edible.")]
        [Min(0f)]
        [SerializeField] private float hungerRestore;

        [Tooltip("Thirst restored when consumed (category Consumable). Set on drinks and juicy food; 0 = restores no thirst.")]
        [Min(0f)]
        [SerializeField] private float thirstRestore;

        [Header("Stat Modifiers (While Equipped)")]
        [Tooltip("Stat modifiers active while this item is the equipped hotbar item (a pickaxe's mining_speed bonus, a future backpack's carry_capacity). Applied/removed by EquippedItemStatModifiers on equip change.")]
        [SerializeField] private List<EquipStatModifier> equipStatModifiers = new List<EquipStatModifier>();

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

        public bool IsRangedWeapon => isRangedWeapon;
        public float ProjectileSpeed => projectileSpeed;
        public ItemDefinition AmmoItem => ammoItem;

        /// <summary>Hunger restored when eaten; 0 = not edible.</summary>
        public float HungerRestore => hungerRestore;

        /// <summary>Thirst restored when consumed; 0 = restores no thirst.</summary>
        public float ThirstRestore => thirstRestore;

        /// <summary>Stat modifiers active while equipped (see EquippedItemStatModifiers).</summary>
        public IReadOnlyList<EquipStatModifier> EquipStatModifiers => equipStatModifiers;

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

        /// <summary>True when this item wears out with use: durability authored AND it's a tool or weapon.</summary>
        public bool HasDurability => maxDurability > 0f && (isTool || isWeapon);

        /// <summary>Durability points at full condition; 0 = never degrades.</summary>
        public float MaxDurability => maxDurability;

        public float DurabilityPerMiningHit => durabilityPerMiningHit;
        public float DurabilityPerAttackHit => durabilityPerAttackHit;
        public ItemBreakBehavior BreakBehavior => breakBehavior;

        /// <summary>Replacement item on break when BreakBehavior is DowngradeToBrokenVariant.</summary>
        public ItemDefinition BrokenVariant => brokenVariant;

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
