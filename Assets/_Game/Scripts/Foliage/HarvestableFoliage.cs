using IslandGame.Data.Foliage;
using IslandGame.Interaction;
using IslandGame.Inventory;
using IslandGame.Sky;
using UnityEngine;

namespace IslandGame.Foliage
{
    /// <summary>
    /// A pickable plant: aim, press E, get the definition's yield. This is
    /// the light-weight world-object interaction the foliage system
    /// establishes — a direct IInteractable pick through the SAME
    /// PlayerInteraction input path the functional placeables use, and
    /// deliberately NOT the tool-gated block-mining pipeline (no tool, no
    /// hardness, no voxel edits).
    ///
    /// DEPLETION: instead of destroying the object, harvesting switches it to
    /// a picked-clean visual state in place — the YieldVisualRoot child
    /// (berry clusters, tall reed stalks) is hidden, and the definition's
    /// optional DepletedMaterial is swapped onto the flagged renderers. A
    /// regrowth timer in IN-GAME hours (TimeOfDayController's clock, so
    /// sleeping and F11 fast-forward advance it; falls back to a 20-minute
    /// real-time day when no controller exists) restores the plant
    /// automatically. Depleted plants report a null prompt: no HUD hint, and
    /// Interact no-ops.
    ///
    /// Works standalone on a hand-placed prefab (serialized definition) and
    /// under the FoliageScatterSystem, which pools instances and carries the
    /// depleted state across despawn/respawn via IsDepleted/RegrowAtWorldDays
    /// and RestoreDepleted.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HarvestableFoliage : MonoBehaviour, IInteractable
    {
        /// <summary>Real seconds per game day when no TimeOfDayController exists (matches the controller's 20-minute default).</summary>
        private const float FallbackDayLengthSeconds = 1200f;

        /// <summary>Regrowth poll cadence while depleted, seconds — cheap and plenty for hour-scale timers.</summary>
        private const float RegrowCheckInterval = 0.25f;

        [Tooltip("The foliage variety this plant is. Wired by the content creator on the prefab; the scatter system re-Initializes pooled instances with the same definition.")]
        [SerializeField] private FoliageDefinition definition;

        [Tooltip("Child holding the visuals that vanish when picked (berry clusters, tall reed stalks) and reappear on regrowth. Optional — null means only the material swap marks depletion.")]
        [SerializeField] private GameObject yieldVisualRoot;

        [Tooltip("Renderers that get the definition's Depleted Material while picked clean (the body reads duller/sparser). Optional — empty means only the yield visuals toggle.")]
        [SerializeField] private Renderer[] depletionSwapRenderers = new Renderer[0];

        private Material[] originalMaterials;
        private bool depleted;
        private double regrowAtWorldDays;
        private float nextRegrowCheckTime;

        private static TimeOfDayController clock;
        private static float nextClockSearchTime;

        /// <summary>The variety this plant is (the scatter system reads it for pooling).</summary>
        public FoliageDefinition Definition => definition;

        /// <summary>True while picked clean and waiting on regrowth.</summary>
        public bool IsDepleted => depleted;

        /// <summary>When (in world days, TimeOfDayController scale) a depleted plant regrows. Meaningless while grown.</summary>
        public double RegrowAtWorldDays => regrowAtWorldDays;

        // ------------------------------------------------------------------
        // IInteractable — the direct pick
        // ------------------------------------------------------------------

        public string InteractionPrompt =>
            definition != null && definition.HasYield && !depleted
                ? $"Harvest {definition.DisplayName}"
                : null;

        public void Interact(GameObject interactor)
        {
            if (depleted || definition == null || !definition.HasYield)
                return;

            var inventory = interactor.GetComponent<InventorySystem>();
            if (inventory == null)
                return;

            int count = Random.Range(definition.YieldCountMin, definition.YieldCountMax + 1);
            if (count > 0)
            {
                int stored = inventory.AddItem(definition.YieldItem, count);

                // Full inventory never wastes the pick: the remainder drops at
                // the plant through the same WorldItem path mining drops use.
                int leftover = count - stored;
                if (leftover > 0)
                {
                    WorldItem.Spawn(
                        definition.YieldItem, leftover, 1f,
                        transform.position + Vector3.up * 0.6f,
                        Vector3.up * 1.5f);
                }
            }

            // An unlucky 0-count roll still picks the plant clean — the player
            // action happened; the bush regrows either way.
            regrowAtWorldDays = NowWorldDays + definition.RegrowHours / 24.0;
            SetDepleted(true);
        }

        // ------------------------------------------------------------------
        // Scatter-system lifecycle (also safe for hand-placed instances)
        // ------------------------------------------------------------------

        /// <summary>Fresh spawn / pooled reuse: binds the definition and resets to the fully-grown state.</summary>
        public void Initialize(FoliageDefinition foliageDefinition)
        {
            definition = foliageDefinition;
            SetDepleted(false);
        }

        /// <summary>
        /// Respawn of a plant that was depleted when it streamed out. A regrow
        /// time already in the past means it regrew while despawned — it comes
        /// back fully grown.
        /// </summary>
        public void RestoreDepleted(double savedRegrowAtWorldDays)
        {
            if (NowWorldDays >= savedRegrowAtWorldDays)
            {
                SetDepleted(false);
                return;
            }

            regrowAtWorldDays = savedRegrowAtWorldDays;
            SetDepleted(true);
        }

        private void Awake()
        {
            // Original materials are captured once per instance — pooled
            // reactivations skip Awake and keep this cache.
            originalMaterials = new Material[depletionSwapRenderers.Length];
            for (int i = 0; i < depletionSwapRenderers.Length; i++)
            {
                if (depletionSwapRenderers[i] != null)
                    originalMaterials[i] = depletionSwapRenderers[i].sharedMaterial;
            }
        }

        private void Update()
        {
            if (!depleted || Time.time < nextRegrowCheckTime)
                return;

            nextRegrowCheckTime = Time.time + RegrowCheckInterval;
            if (NowWorldDays >= regrowAtWorldDays)
                SetDepleted(false);
        }

        // ------------------------------------------------------------------
        // Visual state
        // ------------------------------------------------------------------

        private void SetDepleted(bool value)
        {
            depleted = value;

            if (yieldVisualRoot != null)
                yieldVisualRoot.SetActive(!value);

            Material swap = definition != null ? definition.DepletedMaterial : null;
            for (int i = 0; i < depletionSwapRenderers.Length; i++)
            {
                Renderer swapRenderer = depletionSwapRenderers[i];
                if (swapRenderer == null)
                    continue;

                if (value && swap != null)
                    swapRenderer.sharedMaterial = swap;
                else if (originalMaterials != null && originalMaterials[i] != null)
                    swapRenderer.sharedMaterial = originalMaterials[i];
            }
        }

        // ------------------------------------------------------------------
        // World clock (game time, so time skips advance regrowth)
        // ------------------------------------------------------------------

        /// <summary>
        /// Continuous world time in days: DayNumber + TimeOfDay01 from the
        /// scene's TimeOfDayController (found lazily, re-searched at most
        /// every 5 s so a controller-less scene doesn't scan every frame).
        /// </summary>
        private static double NowWorldDays
        {
            get
            {
                if (clock == null && Time.unscaledTime >= nextClockSearchTime)
                {
                    nextClockSearchTime = Time.unscaledTime + 5f;
                    clock = FindFirstObjectByType<TimeOfDayController>();
                }

                return clock != null
                    ? clock.DayNumber + (double)clock.TimeOfDay01
                    : Time.time / FallbackDayLengthSeconds;
            }
        }
    }
}
