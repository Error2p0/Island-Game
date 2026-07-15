using IslandGame.Data.Stats;
using IslandGame.Sky;
using IslandGame.Stats;
using UnityEngine;

namespace IslandGame.Player
{
    /// <summary>
    /// Weather's bite, expressed EXACTLY like PlayerSurvival's rules: one
    /// StatModifier per condition, applied/removed on state edges from a slow
    /// tick, all through the existing modifier system — no second temperature
    /// mechanic, the Warmth stat is the temperature. A separate component
    /// (not more rules inside PlayerSurvival) so weather carries its own
    /// modifier SOURCE — the shared "Weather" tag — and its teardown clears
    /// exactly its own effects; PlayerSurvival's tuned night/campfire rules
    /// are untouched and STACK with these:
    ///
    ///   RAIN, EXPOSED  — an extra negative warmth-regen modifier on top of
    ///     whatever day/night already applies (a rainy night is colder than
    ///     a clear one; a rainy day can still beat warmth's daytime regen).
    ///   STORM, EXPOSED — a stronger drain replaces the rain one.
    ///   SHELTERED      — under any roof (WeatherShelter's upward probe:
    ///     building pieces, ruins, cave ceilings, terrain overhangs) the
    ///     weather drain is REMOVED ENTIRELY. Shelter beats weather — that
    ///     is the value proposition of building a roof, and a lit campfire
    ///     under it warms you back up through the existing campfire rule.
    ///
    /// Drain magnitudes ramp with Precipitation01/StormIntensity01 rather
    /// than snapping: the modifier is re-issued only when its rounded value
    /// actually changes (quarter-point steps), so light drizzle at a front's
    /// edge nips instead of freezing, without per-frame modifier churn.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(StatContainer))]
    public sealed class WeatherSurvivalEffects : MonoBehaviour
    {
        [Header("Warmth Drain (regen reduction while EXPOSED, full intensity)")]
        [Tooltip("Warmth-regen reduction in full rain. Tune against PlayerSurvival's night reduction (default 0.85) — rain should sting more than a clear night.")]
        [Min(0f)]
        [SerializeField] private float rainWarmthRegenReduction = 1.2f;

        [Tooltip("Warmth-regen reduction in a full storm. Should make even daytime exposure a real problem.")]
        [Min(0f)]
        [SerializeField] private float stormWarmthRegenReduction = 2.4f;

        [Header("Ticking")]
        [Tooltip("Seconds between condition re-evaluations (shelter raycast + intensity step). Matches PlayerSurvival's pacing.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float conditionCheckInterval = 0.25f;

        /// <summary>
        /// The shared source tag for every weather-applied modifier — one
        /// interned string, so RemoveAllFromSource's reference compare treats
        /// all weather effects as one cleanly removable family.
        /// </summary>
        public static readonly object WeatherModifierSource = "Weather";

        private StatContainer statContainer;
        private WeatherController weather;
        private float nextCheckTime;

        private StatModifier activeDrain;   // the currently-applied warmth drain, null when none
        private float activeDrainValue;

        /// <summary>True while rain/storm is actively draining warmth (HUD "you are wet and cold" hooks later).</summary>
        public bool IsExposedToWeather { get; private set; }

        /// <summary>Last shelter verdict (debug/HUD).</summary>
        public bool IsSheltered { get; private set; }

        private void Awake()
        {
            statContainer = GetComponent<StatContainer>();
        }

        private void OnDisable()
        {
            statContainer.RemoveAllFromSource(WeatherModifierSource);
            activeDrain = null;
            activeDrainValue = 0f;
            IsExposedToWeather = false;
        }

        private void Update()
        {
            if (Time.time < nextCheckTime)
                return;

            nextCheckTime = Time.time + conditionCheckInterval;
            EvaluateWeather();
        }

        private void EvaluateWeather()
        {
            if (weather == null)
            {
                weather = WeatherController.Instance;
                if (weather == null)
                    return; // no weather system in this scene — no effects
            }

            // Blend of the two drains by live intensity: a front ramping in
            // drains gently before it drains hard; storm dominates as
            // StormIntensity01 rises. Quantized to quarter points so the
            // modifier swaps a handful of times per front, not per frame.
            float rawDrain = weather.Precipitation01 * rainWarmthRegenReduction
                             + weather.StormIntensity01 * (stormWarmthRegenReduction - rainWarmthRegenReduction);
            float targetDrain = Mathf.Round(rawDrain * 4f) / 4f;

            IsSheltered = WeatherShelter.IsSheltered(transform.position + Vector3.up * 1.4f, transform);
            if (IsSheltered)
                targetDrain = 0f; // a roof stops the rain — full stop, by design

            IsExposedToWeather = targetDrain > 0f;

            if (Mathf.Approximately(targetDrain, activeDrainValue))
                return; // state unchanged — nothing re-applied (the survival-rules convention)

            if (activeDrain != null)
            {
                statContainer.RemoveModifier(StatIds.Warmth, activeDrain);
                activeDrain = null;
            }

            activeDrainValue = targetDrain;
            if (targetDrain <= 0f)
                return;

            // Modifiers are immutable by design — a changed intensity is a
            // remove + add of a fresh instance under the same source tag.
            activeDrain = new StatModifier(
                WeatherModifierSource, StatModifierTarget.RegenRate, StatModifierType.Flat, -targetDrain);
            statContainer.AddModifier(StatIds.Warmth, activeDrain);
        }
    }
}
