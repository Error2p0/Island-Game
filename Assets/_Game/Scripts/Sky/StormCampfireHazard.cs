using IslandGame.Building;
using UnityEngine;

namespace IslandGame.Sky
{
    /// <summary>
    /// The storm's one bit of world mischief: uncovered lit campfires can be
    /// doused by the driving rain. Scans CampfireBehavior.ActiveCampfires
    /// (the existing registry — no physics query, no scene search) on a slow
    /// interval while a storm rages; each lit, UNSHELTERED fire rolls an
    /// extinguish chance per check. Roofed fires are immune — the same
    /// WeatherShelter probe the player's warmth uses, so the two rules can
    /// never disagree about what counts as cover.
    ///
    /// Extinguishing goes through CampfireBehavior.Extinguish(): fuel is
    /// kept (wet wood, not lost wood), LitChanged fires, and relighting is
    /// the normal Interact — deliberately annoying, never punishing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StormCampfireHazard : MonoBehaviour
    {
        [Tooltip("Seconds between douse checks while a storm is at full intensity.")]
        [Range(2f, 60f)]
        [SerializeField] private float checkInterval = 8f;

        [Tooltip("Chance PER CHECK that one uncovered lit campfire is doused. With 8 s checks, 0.2 ≈ an uncovered fire survives a couple of minutes of storm on average.")]
        [Range(0f, 1f)]
        [SerializeField] private float extinguishChancePerCheck = 0.2f;

        [Tooltip("Wired by the builder; auto-resolved when empty.")]
        [SerializeField] private WeatherController weather;

        private float nextCheckTime;

        private void Start()
        {
            if (weather == null)
                weather = GetComponent<WeatherController>();
        }

        private void Update()
        {
            if (weather == null || Time.time < nextCheckTime)
                return;

            nextCheckTime = Time.time + checkInterval;

            // Ramp-in grace: a storm still building (or fading) doesn't douse.
            if (weather.StormIntensity01 < 0.75f)
                return;

            var campfires = CampfireBehavior.ActiveCampfires;
            for (int i = 0; i < campfires.Count; i++)
            {
                CampfireBehavior campfire = campfires[i];
                if (campfire == null || !campfire.IsLit)
                    continue;

                Transform selfRoot = campfire.Piece != null ? campfire.Piece.transform : campfire.transform;
                if (WeatherShelter.IsSheltered(campfire.transform.position + Vector3.up * 0.6f, selfRoot))
                    continue;

                if (Random.value < extinguishChancePerCheck)
                {
                    campfire.Extinguish();
                    Debug.Log("[Weather] The storm doused an uncovered campfire.", campfire);
                }
            }
        }
    }
}
