using IslandGame.Data.Items;
using UnityEngine;

namespace IslandGame.Data.Blocks
{
    /// <summary>
    /// Authored data for one voxel block type. Pure data — the terrain mesher,
    /// mining system and drops read these fields. Created via the asset menu now
    /// and via the Block Editor window from Phase 2 on. Registered in the
    /// BlockDatabase automatically on import.
    ///
    /// TEXTURES: a block either uses one Uniform Texture on all six faces, or
    /// per-face textures. Per-face slots left empty fall back to the uniform
    /// texture, so "grass = green top + dirt bottom + shared sides" only fills
    /// what differs. Textures are plain Texture2D assets; atlas packing and UV
    /// lookup happen at runtime in BlockTextureAtlas — layout is never authored.
    /// </summary>
    [CreateAssetMenu(fileName = "NewBlock", menuName = "Island Game/Block Definition")]
    public sealed class BlockDefinition : ScriptableObject, IDefinition
    {
        [Header("Identity")]
        [Tooltip("Stable unique ID (lowercase_underscore). Serialized into terrain saves — NEVER change it after a world has been saved with it. Auto-filled from the asset name when empty.")]
        [SerializeField] private string id;

        [Tooltip("Name shown in creative menu and tooltips.")]
        [SerializeField] private string displayName;

        [Header("Textures")]
        [Tooltip("On: Uniform Texture covers all six faces (per-face slots ignored). Off: per-face slots are used, empty slots fall back to Uniform Texture.")]
        [SerializeField] private bool useUniformTexture = true;

        [Tooltip("Texture for all faces (or the fallback for empty per-face slots). Import with Read/Write enabled — see BlockTextureAtlas.")]
        [SerializeField] private Texture2D uniformTexture;

        [SerializeField] private Texture2D topTexture;
        [SerializeField] private Texture2D bottomTexture;
        [SerializeField] private Texture2D northTexture;
        [SerializeField] private Texture2D southTexture;
        [SerializeField] private Texture2D eastTexture;
        [SerializeField] private Texture2D westTexture;

        [Header("Physics & Rendering")]
        [Tooltip("On: the block gets collision and blocks movement. Off: walk-through (foliage, liquids).")]
        [SerializeField] private bool isSolid = true;

        [Tooltip("On: neighbors' faces against this block are NOT culled (glass, leaves, water). Off: fully opaque, hidden faces are culled.")]
        [SerializeField] private bool isTransparent;

        [Header("Mining")]
        [Tooltip("Base seconds to mine with a bare hand at tier; tools divide this by their Mining Speed Multiplier. Higher = tougher.")]
        [Min(0f)]
        [SerializeField] private float hardness = 1f;

        [Tooltip("Minimum ItemDefinition.ToolTier required to mine this block at all. 0 = bare hands work.")]
        [Min(0)]
        [SerializeField] private int requiredToolTier;

        [Header("Drops")]
        [Tooltip("Item yielded when this block is mined. Null = drops nothing. This is the block→item half of the link; ItemDefinition.PlacedBlock is the item→block half.")]
        [SerializeField] private ItemDefinition dropItem;

        [Tooltip("Inclusive random range of units dropped.")]
        [Min(0)]
        [SerializeField] private int dropCountMin = 1;

        [Min(0)]
        [SerializeField] private int dropCountMax = 1;

        [Header("Behavior")]
        [Tooltip("Special-behavior markers (liquid, foliage, ...). Combinable; runtime systems query them via HasBehavior.")]
        [SerializeField] private BlockBehaviorFlags behaviorFlags = BlockBehaviorFlags.None;

        public string Id => id;
        public string DisplayName => displayName;

        public bool UseUniformTexture => useUniformTexture;
        public Texture2D UniformTexture => uniformTexture;

        public bool IsSolid => isSolid;
        public bool IsTransparent => isTransparent;

        public float Hardness => hardness;
        public int RequiredToolTier => requiredToolTier;

        public ItemDefinition DropItem => dropItem;
        public int DropCountMin => dropCountMin;
        public int DropCountMax => dropCountMax;

        public BlockBehaviorFlags BehaviorFlags => behaviorFlags;

        public bool HasBehavior(BlockBehaviorFlags flag)
        {
            return (behaviorFlags & flag) != 0;
        }

        /// <summary>
        /// Resolves the texture shown on a face, applying the uniform/per-face
        /// convention (see class summary). May return null when the block is
        /// missing textures entirely — BlockTextureAtlas maps that to its error
        /// tile so the mistake is visible in-world.
        /// </summary>
        public Texture2D GetFaceTexture(BlockFace face)
        {
            if (useUniformTexture)
                return uniformTexture;

            Texture2D perFace;
            switch (face)
            {
                case BlockFace.Top: perFace = topTexture; break;
                case BlockFace.Bottom: perFace = bottomTexture; break;
                case BlockFace.North: perFace = northTexture; break;
                case BlockFace.South: perFace = southTexture; break;
                case BlockFace.East: perFace = eastTexture; break;
                case BlockFace.West: perFace = westTexture; break;
                default: perFace = null; break;
            }

            return perFace != null ? perFace : uniformTexture;
        }

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

            if (dropCountMax < dropCountMin)
                dropCountMax = dropCountMin;
        }
#endif
    }
}
