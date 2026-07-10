using System;
using System.Collections.Generic;
using IslandGame.Data.Items;
using UnityEngine;

namespace IslandGame.Data.Building
{
    /// <summary>
    /// One line of a piece's build cost: an item and how many of it. Same
    /// shape as RecipeIngredient on purpose — Phase 3 turns these authored
    /// counts into real recipe links; until then the placement UI reads them
    /// directly. Pure data, no logic (Phase 1 convention).
    /// </summary>
    [Serializable]
    public sealed class BuildingMaterialCost
    {
        [SerializeField] private ItemDefinition item;

        [Min(1)]
        [SerializeField] private int count = 1;

        public BuildingMaterialCost()
        {
        }

        public BuildingMaterialCost(ItemDefinition item, int count)
        {
            this.item = item;
            this.count = count;
        }

        public ItemDefinition Item => item;
        public int Count => count;
    }

    /// <summary>
    /// Authored data for one placeable building piece (wall, floor, roof,
    /// foundation, door, functional station, ...). Pure data — Phase 2's
    /// placement system instantiates the prefab and mates sockets, Phase 3
    /// binds recipes and functional behavior. Created via the asset menu or
    /// the Building Piece Editor; registered in the BuildingPieceDatabase
    /// automatically on import, same as items/blocks/recipes.
    ///
    /// PREFAB CONTRACT: the placed prefab's ROOT must carry a BuildingPiece
    /// component (weapon hits resolve IDamageable via GetComponentInParent,
    /// and placement initializes health/functional behavior through it) and
    /// every visible child needs a collider. The editor window validates and
    /// can fix both. Socket frames live HERE on the definition, not as prefab
    /// child transforms — see SnapSocket for the full convention.
    /// </summary>
    [CreateAssetMenu(fileName = "NewBuildingPiece", menuName = "Island Game/Building Piece Definition")]
    public sealed class BuildingPieceDefinition : ScriptableObject, IDefinition
    {
        [Header("Identity")]
        [Tooltip("Stable unique ID (lowercase_underscore). Serialized into building save data — NEVER change it after a world has been saved with it. Auto-filled from the asset name when empty.")]
        [SerializeField] private string id;

        [Tooltip("Name shown in the build menu and tooltips.")]
        [SerializeField] private string displayName;

        [Tooltip("Flavor/help text for the build menu tooltip.")]
        [TextArea(2, 4)]
        [SerializeField] private string description;

        [Header("Classification")]
        [Tooltip("Build-menu grouping and per-kind placement rules (foundations touch terrain, etc.). Snapping itself is driven by socket tags only.")]
        [SerializeField] private BuildingCategory category = BuildingCategory.Wall;

        [Tooltip("Build-menu icon. Optional — entries without one show the display name only.")]
        [SerializeField] private Sprite icon;

        [Header("Placement")]
        [Tooltip("The prefab instantiated when this piece is placed. Root must carry a BuildingPiece component; children need colliders.")]
        [SerializeField] private GameObject prefab;

        [Tooltip("Local-space attachment frames and their snap tags — the whole snapping contract. See SnapSocket.")]
        [SerializeField] private List<SnapSocket> sockets = new List<SnapSocket>();

        [Header("Cost")]
        [Tooltip("Items consumed to place this piece. Phase 3 turns these counts into real recipe links; until then the placement UI reads them directly.")]
        [SerializeField] private List<BuildingMaterialCost> materialCost = new List<BuildingMaterialCost>();

        [Header("Durability")]
        [Tooltip("Hit points of the placed piece. The future damage/decay systems read this; BuildingPiece already initializes from it and dies at 0.")]
        [Min(1f)]
        [SerializeField] private float maxHealth = 100f;

        public string Id => id;
        public string DisplayName => displayName;
        public string Description => description;
        public BuildingCategory Category => category;
        public Sprite Icon => icon;
        public GameObject Prefab => prefab;
        public IReadOnlyList<SnapSocket> Sockets => sockets;
        public IReadOnlyList<BuildingMaterialCost> MaterialCost => materialCost;
        public float MaxHealth => maxHealth;

        /// <summary>
        /// True when the prefab or any cost item reference is missing (deleted
        /// asset). The editor flags it; Phase 2 placement refuses to offer it.
        /// </summary>
        public bool HasDanglingReferences
        {
            get
            {
                if (prefab == null)
                    return true;

                for (int i = 0; i < materialCost.Count; i++)
                {
                    if (materialCost[i] == null || materialCost[i].Item == null)
                        return true;
                }

                return false;
            }
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

            if (maxHealth < 1f)
                maxHealth = 1f;
        }
#endif
    }
}
