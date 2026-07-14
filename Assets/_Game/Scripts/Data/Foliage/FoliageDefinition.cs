using IslandGame.Data.Items;
using UnityEngine;

namespace IslandGame.Data.Foliage
{
    /// <summary>
    /// Authored data for one foliage variety (berry bush, shrub, reed
    /// cluster, ...). Pure data in the project's stable-ID pattern: runtime
    /// systems (the foliage scatter system, HarvestableFoliage) only READ
    /// these fields; the FoliageDatabase registers every asset automatically
    /// on import.
    ///
    /// Foliage is deliberately LIGHTER than the other world content: bushes
    /// are plain scene prefabs (no voxels, no BuildingPiece health/registry),
    /// harvested with a direct Interact() pick — not the tool-gated mining
    /// pipeline and not the building system. Depletion swaps visuals in place
    /// (picked-clean look) and a game-time regrowth timer restores the plant
    /// where it stands, so the world never loses its foliage to harvesting.
    /// </summary>
    [CreateAssetMenu(fileName = "NewFoliage", menuName = "Island Game/Foliage Definition")]
    public sealed class FoliageDefinition : ScriptableObject, IDefinition
    {
        [Header("Identity")]
        [Tooltip("Stable unique ID (lowercase_underscore). Serialized into world state — never change it once content references it. Auto-filled from the asset name when empty.")]
        [SerializeField] private string id;

        [Tooltip("Name shown in the harvest prompt and tooling.")]
        [SerializeField] private string displayName;

        [SerializeField] private FoliageCategory category = FoliageCategory.Bush;

        [Header("World Object")]
        [Tooltip("The prefab the scatter system instantiates. Convention: ONE solid collider on the root (aim ray + physical presence), collider-less primitive visuals under it, and — for harvestables — a HarvestableFoliage component wired to this definition.")]
        [SerializeField] private GameObject prefab;

        [Header("Harvest Yield")]
        [Tooltip("Item granted per pick. Null = not harvestable (ambient foliage) — no prompt, no interaction.")]
        [SerializeField] private ItemDefinition yieldItem;

        [Tooltip("Minimum units granted per pick.")]
        [Min(0)]
        [SerializeField] private int yieldCountMin = 1;

        [Tooltip("Maximum units granted per pick.")]
        [Min(0)]
        [SerializeField] private int yieldCountMax = 3;

        [Header("Regrowth")]
        [Tooltip("IN-GAME hours until a picked plant regrows (24 = one full day/night cycle). Game time, not real time — sleeping and time fast-forward advance it.")]
        [Min(0.1f)]
        [SerializeField] private float regrowHours = 12f;

        [Tooltip("Optional picked-clean material swapped onto the renderers HarvestableFoliage flags while depleted (a duller, sparser look). The berry-cluster hide/show is separate — see HarvestableFoliage.YieldVisualRoot.")]
        [SerializeField] private Material depletedMaterial;

        [Header("Scattering")]
        [Tooltip("Which generator surface band this plant grows on (grass land vs the shoreline).")]
        [SerializeField] private FoliageSurface surface = FoliageSurface.Grass;

        [Tooltip("Relative pick weight when the scatter system chooses a variety for an anchor cell (among varieties whose surface rule passes there). 0 = never auto-scattered.")]
        [Min(0f)]
        [SerializeField] private float spawnWeight = 1f;

        public string Id => id;
        public string DisplayName => displayName;
        public FoliageCategory Category => category;
        public GameObject Prefab => prefab;

        public ItemDefinition YieldItem => yieldItem;
        public int YieldCountMin => yieldCountMin;
        public int YieldCountMax => yieldCountMax;

        /// <summary>True when picking this plant grants something — drives the prompt, the interaction and the depleted/regrow cycle.</summary>
        public bool HasYield => yieldItem != null && yieldCountMax > 0;

        /// <summary>In-game hours from pick to regrown.</summary>
        public float RegrowHours => regrowHours;

        /// <summary>Optional depleted-state material variant (null = renderers keep their materials while depleted).</summary>
        public Material DepletedMaterial => depletedMaterial;

        public FoliageSurface Surface => surface;
        public float SpawnWeight => spawnWeight;

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

            if (yieldCountMax < yieldCountMin)
                yieldCountMax = yieldCountMin;
        }
#endif
    }
}
