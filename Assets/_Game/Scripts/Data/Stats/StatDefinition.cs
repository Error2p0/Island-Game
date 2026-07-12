using UnityEngine;

namespace IslandGame.Data.Stats
{
    /// <summary>
    /// Authored data for one stat type (health, stamina, mining speed, ...).
    /// Pure data, same pattern as Item/Block: stable ID + ScriptableObject +
    /// StatDatabase registry, synced automatically on import. Runtime behavior
    /// lives in StatContainer/StatInstance, which read these fields.
    ///
    /// DECAY IS NEGATIVE REGEN: hunger draining over time and stamina
    /// recovering after a sprint are the same field with opposite signs.
    /// One number, one code path — never a separate decay system.
    /// </summary>
    [CreateAssetMenu(fileName = "NewStat", menuName = "Island Game/Stat Definition")]
    public sealed class StatDefinition : ScriptableObject, IDefinition
    {
        [Header("Identity")]
        [Tooltip("Stable unique ID (lowercase_underscore). Serialized into saves and referenced by items' equip modifiers — NEVER change it after content has been saved with it. Auto-filled from the asset name when empty.")]
        [SerializeField] private string id;

        [Tooltip("Name shown on HUD tooltips and future character screens.")]
        [SerializeField] private string displayName;

        [TextArea(2, 4)]
        [SerializeField] private string description;

        [Tooltip("UI grouping only — no runtime logic branches on this.")]
        [SerializeField] private StatCategory category = StatCategory.Vital;

        [Header("Value Model")]
        [Tooltip("Resource = spendable pool (health, stamina): modifiers move the MAXIMUM, current depletes/regens. Attribute = derived number (mining speed): current always equals the modified value.")]
        [SerializeField] private StatKind kind = StatKind.Resource;

        [Tooltip("Unmodified value. For Resource stats this is the unmodified maximum (a full bar); for Attribute stats it is the unmodified value itself.")]
        [SerializeField] private float baseValue = 100f;

        [Tooltip("Absolute lower clamp for the current value AND the modified value. 0 for pools, can be >0 for attributes that must never hit zero (carry capacity).")]
        [SerializeField] private float minValue;

        [Tooltip("Absolute upper clamp on the MODIFIED value — the hard cap no stack of buffs can exceed. Keep comfortably above Base Value to leave buff headroom.")]
        [SerializeField] private float maxValue = 100f;

        [Header("Regen (Resource stats only)")]
        [Tooltip("Units per second toward the modified maximum. 0 = static. NEGATIVE = decay (hunger drain, warmth loss) — decay ignores the regen delay and applies continuously.")]
        [SerializeField] private float regenPerSecond;

        [Tooltip("Seconds after the last DECREASE before positive regen resumes (stamina-style pause after spending). 0 = regen never pauses.")]
        [Min(0f)]
        [SerializeField] private float regenDelaySeconds;

        [Header("HUD")]
        [Tooltip("Whether the stats HUD builder creates a bar for this stat.")]
        [SerializeField] private bool showOnHud = true;

        [Tooltip("Fill color of this stat's HUD bar.")]
        [SerializeField] private Color barColor = new Color(0.85f, 0.3f, 0.25f);

        public string Id => id;
        public string DisplayName => displayName;
        public string Description => description;
        public StatCategory Category => category;
        public StatKind Kind => kind;

        /// <summary>Unmodified maximum (Resource) or unmodified value (Attribute).</summary>
        public float BaseValue => baseValue;

        public float MinValue => minValue;

        /// <summary>Hard cap on the modified value — the ceiling for buff stacking.</summary>
        public float MaxValue => maxValue;

        /// <summary>Units per second; negative = decay. See class summary.</summary>
        public float RegenPerSecond => regenPerSecond;

        /// <summary>Pause after a decrease before positive regen resumes.</summary>
        public float RegenDelaySeconds => regenDelaySeconds;

        public bool ShowOnHud => showOnHud;
        public Color BarColor => barColor;

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

            if (maxValue < minValue)
                maxValue = minValue;
        }
#endif
    }
}
