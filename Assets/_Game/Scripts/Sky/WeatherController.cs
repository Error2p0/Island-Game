using System;
using IslandGame.Terrain;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IslandGame.Sky
{
    /// <summary>
    /// The weather of the world: a small seeded state machine (Clear → Rain
    /// ⇄ Storm) layered on top of the day/night atmosphere — the same event
    /// pattern as TimeOfDayController, deliberately its sibling on the
    /// DayNight object. Pure state — everything you SEE and FEEL is
    /// WeatherVisuals / WeatherSurvivalEffects / StormCampfireHazard reading
    /// from here.
    ///
    /// TRANSITIONS are drawn from a System.Random seeded off the world seed
    /// (same island, same moods per session) with per-state average durations
    /// jittered ×[0.5, 1.5) — organic, never metronomic. Storms only build
    /// out of rain and decay back through it, so weather always ramps.
    ///
    /// EVENTS fire on every state change INCLUDING debug/scripted forces —
    /// the deliberate opposite of SetTimeOfDay's silent jumps: weather
    /// consumers are few and edge-driven (audio stingers, HUD), and a forced
    /// storm that skipped OnStormStart would be indistinguishable from a
    /// bug. Continuous consumers should instead read the SMOOTHED signals:
    ///   Precipitation01  — rain amount (drives particles, fog, warmth)
    ///   WindStrength01   — smoothed base wind × live gust noise (sway, rain tilt)
    ///   StormIntensity01 — 0→1 blend into/out of Storm (lightning, extra dark)
    /// which ramp over a few seconds so nothing visually snaps.
    ///
    /// SAVE: session-only on purpose (weather re-rolls each load — same
    /// tradeoff as wild creatures/foliage state). A future phase can persist
    /// state + elapsed time additively.
    ///
    /// DEBUG (editor/testing): F8 cycles Clear → Rain → Storm. Key read
    /// directly, same justification as TimeOfDayController's F10/F11 —
    /// diagnostics don't belong on PlayerInputHandler's gameplay surface.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeatherController : MonoBehaviour
    {
        [Header("State Durations (real minutes, jittered ×0.5–1.5)")]
        [Tooltip("Average minutes of clear sky between weather fronts.")]
        [Range(0.5f, 60f)]
        [SerializeField] private float clearAverageMinutes = 6f;

        [Tooltip("Average minutes a rain front lasts.")]
        [Range(0.5f, 30f)]
        [SerializeField] private float rainAverageMinutes = 3f;

        [Tooltip("Average minutes a storm rages.")]
        [Range(0.5f, 30f)]
        [SerializeField] private float stormAverageMinutes = 1.5f;

        [Header("Transition Chances")]
        [Tooltip("Chance a rain front escalates into a storm instead of clearing.")]
        [Range(0f, 1f)]
        [SerializeField] private float rainToStormChance = 0.35f;

        [Tooltip("Chance a storm decays into rain instead of breaking straight to clear sky.")]
        [Range(0f, 1f)]
        [SerializeField] private float stormToRainChance = 0.65f;

        [Header("Signal Smoothing")]
        [Tooltip("Seconds for precipitation/wind/storm signals to ramp between states — rain fades in, it doesn't snap on.")]
        [Range(0.5f, 20f)]
        [SerializeField] private float signalRampSeconds = 5f;

        [Header("Wind (per state, before gusting)")]
        [Range(0f, 1f)]
        [SerializeField] private float clearWind = 0.08f;

        [Range(0f, 1f)]
        [SerializeField] private float rainWind = 0.45f;

        [Range(0f, 1f)]
        [SerializeField] private float stormWind = 1f;

        [Header("Debug")]
        [Tooltip("F8 cycles Clear → Rain → Storm. Disable for release builds.")]
        [SerializeField] private bool enableDebugKeys = true;

        /// <summary>Fires on every state change (natural, forced or debug) with the new state.</summary>
        public event Action<WeatherState> OnWeatherChanged;

        /// <summary>Fires when a storm begins (from any previous state).</summary>
        public event Action OnStormStart;

        /// <summary>Fires when a storm ends (to any next state).</summary>
        public event Action OnStormEnd;

        /// <summary>The discrete state. For anything continuous prefer the smoothed signals below.</summary>
        public WeatherState CurrentState { get; private set; } = WeatherState.Clear;

        /// <summary>True during Rain OR Storm (storms are rain plus violence).</summary>
        public bool IsPrecipitating => CurrentState != WeatherState.Clear;

        public bool IsStorm => CurrentState == WeatherState.Storm;

        /// <summary>Smoothed 0..1 rain amount — particles, fog, rain audio and warmth rules all key off this one signal.</summary>
        public float Precipitation01 { get; private set; }

        /// <summary>Smoothed 0..1 blend into Storm — lightning frequency, extra darkening, camera sway.</summary>
        public float StormIntensity01 { get; private set; }

        /// <summary>Smoothed per-state wind multiplied by live gust noise — foliage sway and rain tilt read this every frame.</summary>
        public float WindStrength01 { get; private set; }

        /// <summary>Seconds until the next natural transition (debug/HUD).</summary>
        public float TimeUntilChange => Mathf.Max(0f, stateEndTime - Time.time);

        /// <summary>The scene's weather, for the many small per-frame readers (foliage sway). Null when no weather system was built.</summary>
        public static WeatherController Instance { get; private set; }

        private System.Random random;
        private float stateEndTime;
        private float smoothedWind;

        private void OnEnable()
        {
            if (Instance != null && Instance != this)
                Debug.LogWarning("[Weather] A second WeatherController is active — the newest one wins the static Instance.", this);
            Instance = this;
        }

        private void OnDisable()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Start()
        {
            // Seeded off the world so a given island has its own weather
            // temperament; falls back to the clock when no voxel world exists
            // (test scenes). XOR keeps it distinct from every other consumer
            // of the raw seed.
            var world = FindFirstObjectByType<VoxelWorld>();
            int seed = world != null && world.ActiveIslandGenerator != null
                ? world.ActiveIslandGenerator.Seed ^ 0x57EA7    // "WEAT"-ish salt
                : Environment.TickCount;
            random = new System.Random(seed);

            ScheduleStateEnd(WeatherState.Clear);
        }

        private void Update()
        {
            if (enableDebugKeys && Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
            {
                var next = (WeatherState)(((int)CurrentState + 1) % 3);
                Debug.Log($"[Weather] Debug force: {CurrentState} → {next}.");
                ForceWeather(next);
            }

            if (Time.time >= stateEndTime)
                SetState(RollNextState());

            UpdateSignals();
        }

        /// <summary>
        /// Scripted/debug jump. Unlike SetTimeOfDay this DOES fire the events
        /// (see class summary) and re-rolls a natural duration for the forced
        /// state, so forced weather ends organically too.
        /// </summary>
        public void ForceWeather(WeatherState state)
        {
            SetState(state);
        }

        // ------------------------------------------------------------------
        // State machine
        // ------------------------------------------------------------------

        private WeatherState RollNextState()
        {
            switch (CurrentState)
            {
                case WeatherState.Clear:
                    return WeatherState.Rain;

                case WeatherState.Rain:
                    return random.NextDouble() < rainToStormChance ? WeatherState.Storm : WeatherState.Clear;

                default: // Storm
                    return random.NextDouble() < stormToRainChance ? WeatherState.Rain : WeatherState.Clear;
            }
        }

        private void SetState(WeatherState state)
        {
            WeatherState previous = CurrentState;
            CurrentState = state;
            ScheduleStateEnd(state);

            if (previous == state)
                return; // re-forcing the same state only re-rolls its duration

            OnWeatherChanged?.Invoke(state);
            if (state == WeatherState.Storm)
                OnStormStart?.Invoke();
            else if (previous == WeatherState.Storm)
                OnStormEnd?.Invoke();
        }

        private void ScheduleStateEnd(WeatherState state)
        {
            float averageMinutes = state == WeatherState.Clear ? clearAverageMinutes
                : state == WeatherState.Rain ? rainAverageMinutes
                : stormAverageMinutes;

            // Random can be null when ForceWeather runs before Start (builder
            // scripts, tests) — the schedule is re-derived in Start anyway.
            double jitter = random != null ? 0.5 + random.NextDouble() : 1.0;
            stateEndTime = Time.time + averageMinutes * 60f * (float)jitter;
        }

        // ------------------------------------------------------------------
        // Smoothed signals
        // ------------------------------------------------------------------

        private void UpdateSignals()
        {
            float ramp = Time.deltaTime / Mathf.Max(0.1f, signalRampSeconds);

            float precipitationTarget = CurrentState == WeatherState.Storm ? 1f
                : CurrentState == WeatherState.Rain ? 0.55f
                : 0f;
            Precipitation01 = Mathf.MoveTowards(Precipitation01, precipitationTarget, ramp);

            StormIntensity01 = Mathf.MoveTowards(StormIntensity01, IsStorm ? 1f : 0f, ramp);

            float windTarget = CurrentState == WeatherState.Storm ? stormWind
                : CurrentState == WeatherState.Rain ? rainWind
                : clearWind;
            smoothedWind = Mathf.MoveTowards(smoothedWind, windTarget, ramp);

            // Gusting: slow Perlin swings ±25% around the smoothed base, so
            // even steady rain breathes. Deterministic enough (shared clock).
            float gust = 0.75f + Mathf.PerlinNoise(Time.time * 0.35f, 0.71f) * 0.5f;
            WindStrength01 = Mathf.Clamp01(smoothedWind * gust);
        }
    }
}
