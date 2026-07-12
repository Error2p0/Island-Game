using IslandGame.Data.Stats;
using UnityEngine;

namespace IslandGame.Stats
{
    /// <summary>
    /// One live modifier on one stat: who applied it (Source), what it targets
    /// (Value or RegenRate), how it combines (Flat or PercentMultiplier), how
    /// much, and optionally for how long. Immutable after construction — to
    /// change a buff, remove it and add a new one, so the instance's cached
    /// math can never go stale silently.
    ///
    /// SOURCE is any object (a component, an item definition, a string tag).
    /// StatContainer.RemoveAllFromSource compares by reference equality, which
    /// lets each feature clear exactly its own modifiers without bookkeeping.
    /// </summary>
    public sealed class StatModifier
    {
        /// <summary>Who applied this (for RemoveAllFromSource). Never null.</summary>
        public object Source { get; }

        public StatModifierTarget Target { get; }
        public StatModifierType Type { get; }

        /// <summary>Flat: added to base. PercentMultiplier: fraction (0.5 = +50%).</summary>
        public float Value { get; }

        /// <summary>Seconds until auto-removal; 0 or less = permanent until removed.</summary>
        public float DurationSeconds { get; }

        /// <summary>Time.time at which the container auto-removes this; +inf for permanent.</summary>
        internal float ExpireTime { get; set; } = float.PositiveInfinity;

        public bool IsTemporary => DurationSeconds > 0f;

        public StatModifier(
            object source,
            StatModifierTarget target,
            StatModifierType type,
            float value,
            float durationSeconds = 0f)
        {
            Source = source ?? "unknown";
            Target = target;
            Type = type;
            Value = value;
            DurationSeconds = durationSeconds;
        }

        public override string ToString()
        {
            string amount = Type == StatModifierType.Flat ? Value.ToString("0.##") : Value.ToString("+0.#%;-0.#%");
            string duration = IsTemporary ? $" for {DurationSeconds:0.#}s" : string.Empty;
            return $"[{Target} {amount} from {Source}{duration}]";
        }
    }
}
