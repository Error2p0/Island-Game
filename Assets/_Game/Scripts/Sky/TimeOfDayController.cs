using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IslandGame.Sky
{
    /// <summary>
    /// The clock of the world: one normalized time-of-day value (0 =
    /// midnight, 0.25 = sunrise, 0.5 = noon, 0.75 = sunset) advancing over a
    /// configurable real-time day length, plus the phase events other systems
    /// hook into (creature spawns, campfire auto-light, ...). Pure time — the
    /// sun, sky, ambient and stars are DayNightVisuals' job, reading
    /// TimeOfDay01 from here.
    ///
    /// EVENTS fire when time NATURALLY crosses the four phase thresholds
    /// (sunrise → day → sunset → night), including several at once during a
    /// fast-forwarded frame. SetTimeOfDay jumps deliberately fire nothing:
    /// a debug scrub to noon shouldn't run every reaction in between — it
    /// resynchronizes silently and consumers re-read the properties.
    ///
    /// DEBUG (editor/testing): F10 toggles pause, holding F11 fast-forwards
    /// (Fast Forward Multiplier ×). Keys are read directly on purpose —
    /// diagnostics don't belong on PlayerInputHandler's gameplay surface.
    /// Context-menu jumps (morning/sunset/midnight) work in the inspector.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TimeOfDayController : MonoBehaviour
    {
        [Header("Cycle")]
        [Tooltip("Real-time minutes for one full day+night cycle.")]
        [Range(1f, 120f)]
        [SerializeField] private float dayLengthMinutes = 20f;

        [Tooltip("Time of day on scene start. 0 = midnight, 0.25 = sunrise, 0.5 = noon, 0.75 = sunset.")]
        [Range(0f, 1f)]
        [SerializeField] private float startTimeOfDay = 0.3f;

        [Header("Phase Thresholds (normalized; keep in ascending order)")]
        [Tooltip("OnSunrise fires here — first light, sun about to clear the horizon.")]
        [Range(0f, 1f)]
        [SerializeField] private float sunriseTime = 0.23f;

        [Tooltip("OnDayStart fires here — sun fully up, night reactions should stand down.")]
        [Range(0f, 1f)]
        [SerializeField] private float dayTime = 0.29f;

        [Tooltip("OnSunset fires here — golden hour begins.")]
        [Range(0f, 1f)]
        [SerializeField] private float sunsetTime = 0.71f;

        [Tooltip("OnNightStart fires here — dark enough for night systems (spawns, fear, campfire relevance).")]
        [Range(0f, 1f)]
        [SerializeField] private float nightTime = 0.79f;

        [Header("Debug")]
        [Tooltip("F10 = pause, hold F11 = fast-forward. Disable for release builds.")]
        [SerializeField] private bool enableDebugKeys = true;

        [Tooltip("Time multiplier while F11 is held.")]
        [Min(1f)]
        [SerializeField] private float fastForwardMultiplier = 120f;

        /// <summary>First light (default 0.23).</summary>
        public event Action OnSunrise;

        /// <summary>Sun fully up (default 0.29).</summary>
        public event Action OnDayStart;

        /// <summary>Golden hour begins (default 0.71).</summary>
        public event Action OnSunset;

        /// <summary>Darkness sets in (default 0.79).</summary>
        public event Action OnNightStart;

        /// <summary>0 = midnight, 0.25 = sunrise, 0.5 = noon, 0.75 = sunset.</summary>
        public float TimeOfDay01 { get; private set; }

        /// <summary>How many full cycles have completed since play started.</summary>
        public int DayNumber { get; private set; }

        public bool IsPaused { get; private set; }

        /// <summary>True between NightStart and Sunrise.</summary>
        public bool IsNight => TimeOfDay01 < sunriseTime || TimeOfDay01 >= nightTime;

        /// <summary>Seconds of real time per full cycle.</summary>
        public float DayLengthSeconds => dayLengthMinutes * 60f;

        private void Awake()
        {
            TimeOfDay01 = Mathf.Repeat(startTimeOfDay, 1f);
        }

        private void Update()
        {
            float speed = 1f;

            if (enableDebugKeys && Keyboard.current != null)
            {
                if (Keyboard.current.f10Key.wasPressedThisFrame)
                {
                    IsPaused = !IsPaused;
                    Debug.Log($"TimeOfDay: {(IsPaused ? "paused" : "resumed")} at {TimeOfDay01:0.000}.");
                }

                if (Keyboard.current.f11Key.isPressed)
                    speed = fastForwardMultiplier;
            }

            if (IsPaused)
                return;

            float previous = TimeOfDay01;
            float advanced = previous + Time.deltaTime * speed / DayLengthSeconds;

            if (advanced >= 1f)
                DayNumber++;

            TimeOfDay01 = Mathf.Repeat(advanced, 1f);

            FireIfCrossed(previous, advanced, sunriseTime, OnSunrise);
            FireIfCrossed(previous, advanced, dayTime, OnDayStart);
            FireIfCrossed(previous, advanced, sunsetTime, OnSunset);
            FireIfCrossed(previous, advanced, nightTime, OnNightStart);
        }

        /// <summary>
        /// Debug/scripted jump. Deliberately fires NO phase events (see class
        /// summary); consumers re-read TimeOfDay01/IsNight after a jump.
        /// </summary>
        public void SetTimeOfDay(float normalizedTime)
        {
            TimeOfDay01 = Mathf.Repeat(normalizedTime, 1f);
        }

        public void SetPaused(bool paused)
        {
            IsPaused = paused;
        }

        /// <summary>
        /// Load-time restore: time AND day counter in one silent jump (same
        /// no-events rule as SetTimeOfDay — consumers re-read properties).
        /// </summary>
        public void RestoreTime(float normalizedTime, int dayNumber)
        {
            TimeOfDay01 = Mathf.Repeat(normalizedTime, 1f);
            DayNumber = Mathf.Max(0, dayNumber);
        }

        /// <summary>
        /// `previous` is unwrapped (may exceed 1) so a threshold is crossed
        /// iff it lies in (previous, advanced] — handles midnight wrap and
        /// multiple thresholds inside one fast-forwarded frame alike.
        /// </summary>
        private static void FireIfCrossed(float previous, float advanced, float threshold, Action handler)
        {
            if (handler == null)
                return;

            // Compare in the un-wrapped domain: lift the threshold into
            // [previous, previous+1) and test against the advance distance.
            float lifted = threshold;
            while (lifted <= previous)
                lifted += 1f;

            if (lifted <= advanced)
                handler.Invoke();
        }

        [ContextMenu("Jump To Morning (0.30)")]
        private void JumpToMorning() => SetTimeOfDay(0.30f);

        [ContextMenu("Jump To Sunset (0.72)")]
        private void JumpToSunset() => SetTimeOfDay(0.72f);

        [ContextMenu("Jump To Midnight (0.00)")]
        private void JumpToMidnight() => SetTimeOfDay(0f);
    }
}
