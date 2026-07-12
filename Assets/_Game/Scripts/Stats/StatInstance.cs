using System.Collections.Generic;
using IslandGame.Data.Stats;
using UnityEngine;

namespace IslandGame.Stats
{
    /// <summary>
    /// Runtime state of one stat on one StatContainer: current value, the
    /// modifier stack, and the cached modified value/regen. Plain class, no
    /// Unity lifecycle — the owning StatContainer drives ticking and raises
    /// the events; this class only reports what changed via return values.
    ///
    /// PERFORMANCE: modified value and regen are cached and recomputed only
    /// when the modifier stack changes (dirty flag), so the per-frame cost of
    /// a stat is one branch when idle and a couple of float ops while
    /// regenerating — safe for many creatures each carrying a container.
    /// </summary>
    public sealed class StatInstance
    {
        public StatDefinition Definition { get; }

        /// <summary>Current value. Resource: within [min, ModifiedValue]. Attribute: always == ModifiedValue.</summary>
        public float Current { get; private set; }

        /// <summary>True after Current reached the min clamp, until it rises above it (drives OnStatDepleted edges).</summary>
        public bool IsDepleted { get; private set; }

        /// <summary>True after Current reached the modified maximum (drives OnStatFull edges). Attributes are never "full".</summary>
        public bool IsFull { get; private set; }

        private readonly List<StatModifier> modifiers = new List<StatModifier>();
        private readonly float baseValue;
        private float cachedValue;
        private float cachedRegen;
        private bool dirty = true;
        private float lastDecreaseTime = float.NegativeInfinity;

        /// <summary>
        /// The unmodified base in effect: the definition's authored value, or
        /// the per-owner override (creature species share stat DEFINITIONS
        /// but differ in base values — a deer's health ≠ a boar's).
        /// </summary>
        public float BaseValue => baseValue;

        public StatInstance(StatDefinition definition, float baseValueOverride = float.NaN)
        {
            Definition = definition;
            baseValue = float.IsNaN(baseValueOverride) ? definition.BaseValue : baseValueOverride;
            RecomputeIfDirty();
            // Resources spawn full (a fresh player has a full bar); attributes
            // start at their modified value by definition.
            Current = cachedValue;
            IsFull = definition.Kind == StatKind.Resource;
        }

        /// <summary>Modified maximum (Resource) or modified value (Attribute): (base + Σflat) * (1 + Σpercent), clamped to [min, max].</summary>
        public float ModifiedValue
        {
            get
            {
                RecomputeIfDirty();
                return cachedValue;
            }
        }

        /// <summary>Modified regen per second; negative = decay.</summary>
        public float RegenPerSecond
        {
            get
            {
                RecomputeIfDirty();
                return cachedRegen;
            }
        }

        /// <summary>Current mapped to 0..1 between the min clamp and the modified maximum (HUD bars).</summary>
        public float Normalized
        {
            get
            {
                float min = Definition.MinValue;
                float max = ModifiedValue;
                return max - min <= 0f ? 0f : Mathf.Clamp01((Current - min) / (max - min));
            }
        }

        /// <summary>Live modifiers, for debugging/inspection. Do not mutate.</summary>
        public IReadOnlyList<StatModifier> Modifiers => modifiers;

        // ------------------------------------------------------------------
        // Mutations (called by StatContainer, which raises the events)
        // ------------------------------------------------------------------

        /// <summary>
        /// Applies a delta to Current (damage, spending, meals). Only valid for
        /// Resource stats — attributes change through modifiers. Returns true
        /// when the value actually changed, with old/new for the event.
        /// </summary>
        internal bool ApplyDelta(float delta, float now, out float oldValue, out float newValue)
        {
            oldValue = Current;
            newValue = Current;

            if (Definition.Kind != StatKind.Resource || delta == 0f)
                return false;

            newValue = Mathf.Clamp(Current + delta, Definition.MinValue, ModifiedValue);
            if (Mathf.Approximately(newValue, oldValue))
            {
                newValue = oldValue;
                return false;
            }

            if (newValue < oldValue)
                lastDecreaseTime = now;

            Current = newValue;
            return true;
        }

        /// <summary>Sets Current directly (respawn refills, save/load later). Clamped; Resource stats only.</summary>
        internal bool SetCurrent(float value, float now, out float oldValue, out float newValue)
        {
            return ApplyDelta(value - Current, now, out oldValue, out newValue);
        }

        /// <summary>
        /// Per-frame regen/decay for Resource stats. Positive net regen waits
        /// out the regen delay after the last decrease; negative net regen
        /// (decay) applies continuously — a starving stomach doesn't pause
        /// because you also just ate a little.
        /// </summary>
        internal bool Tick(float deltaTime, float now, out float oldValue, out float newValue)
        {
            oldValue = Current;
            newValue = Current;

            if (Definition.Kind != StatKind.Resource)
                return false;

            float regen = RegenPerSecond;
            if (regen == 0f)
                return false;

            if (regen > 0f)
            {
                if (Current >= ModifiedValue)
                    return false;
                if (now - lastDecreaseTime < Definition.RegenDelaySeconds)
                    return false;
            }
            else if (Current <= Definition.MinValue)
            {
                return false;
            }

            // Regen must not stamp lastDecreaseTime, or decay would eternally
            // re-arm the regen delay against itself — bypass ApplyDelta's
            // decrease tracking for tick movement.
            newValue = Mathf.Clamp(Current + regen * deltaTime, Definition.MinValue, ModifiedValue);
            if (Mathf.Approximately(newValue, oldValue))
            {
                newValue = oldValue;
                return false;
            }

            Current = newValue;
            return true;
        }

        /// <summary>
        /// Adds a modifier and reconciles Current with the new modified value.
        /// Returns true when Current changed (max clamp, or attribute value
        /// moved) so the container can raise OnStatChanged.
        /// </summary>
        internal bool AddModifier(StatModifier modifier, float now, out float oldValue, out float newValue)
        {
            modifiers.Add(modifier);
            modifier.ExpireTime = modifier.IsTemporary ? now + modifier.DurationSeconds : float.PositiveInfinity;
            dirty = true;
            return ReconcileCurrent(out oldValue, out newValue);
        }

        /// <summary>Removes a specific modifier instance. Same reconciliation contract as AddModifier.</summary>
        internal bool RemoveModifier(StatModifier modifier, out float oldValue, out float newValue)
        {
            if (!modifiers.Remove(modifier))
            {
                oldValue = newValue = Current;
                return false;
            }

            dirty = true;
            return ReconcileCurrent(out oldValue, out newValue);
        }

        /// <summary>Removes every modifier whose Source is the given object (reference equality). Returns removed count.</summary>
        internal int RemoveAllFromSource(object source, out float oldValue, out float newValue, out bool currentChanged)
        {
            oldValue = Current;
            newValue = Current;

            int removed = 0;
            for (int i = modifiers.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(modifiers[i].Source, source))
                {
                    modifiers.RemoveAt(i);
                    removed++;
                }
            }

            if (removed > 0)
                dirty = true;

            currentChanged = removed > 0 && ReconcileCurrent(out oldValue, out newValue);
            return removed;
        }

        /// <summary>Removes modifiers whose ExpireTime has passed. Same reconciliation contract.</summary>
        internal int RemoveExpired(float now, out float oldValue, out float newValue, out bool currentChanged)
        {
            oldValue = Current;
            newValue = Current;

            int removed = 0;
            for (int i = modifiers.Count - 1; i >= 0; i--)
            {
                if (now >= modifiers[i].ExpireTime)
                {
                    modifiers.RemoveAt(i);
                    removed++;
                }
            }

            if (removed > 0)
                dirty = true;

            currentChanged = removed > 0 && ReconcileCurrent(out oldValue, out newValue);
            return removed;
        }

        /// <summary>True when any live modifier can still expire — lets the container skip scanning static stacks.</summary>
        internal bool HasTemporaryModifiers
        {
            get
            {
                for (int i = 0; i < modifiers.Count; i++)
                {
                    if (modifiers[i].IsTemporary)
                        return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Updates depletion/fullness edge flags AFTER the container has raised
        /// OnStatChanged. Returns which edges fired this transition.
        /// </summary>
        internal void UpdateEdgeFlags(out bool becameDepleted, out bool becameFull)
        {
            bool depletedNow = Current <= Definition.MinValue;
            bool fullNow = Definition.Kind == StatKind.Resource && Current >= ModifiedValue;

            becameDepleted = depletedNow && !IsDepleted;
            becameFull = fullNow && !IsFull;
            IsDepleted = depletedNow;
            IsFull = fullNow;
        }

        // ------------------------------------------------------------------
        // Internals
        // ------------------------------------------------------------------

        /// <summary>After a modifier change: attributes snap Current to the new value; resources clamp into the new range.</summary>
        private bool ReconcileCurrent(out float oldValue, out float newValue)
        {
            oldValue = Current;

            newValue = Definition.Kind == StatKind.Attribute
                ? ModifiedValue
                : Mathf.Clamp(Current, Definition.MinValue, ModifiedValue);

            if (Mathf.Approximately(newValue, oldValue))
            {
                newValue = oldValue;
                return false;
            }

            Current = newValue;
            return true;
        }

        private void RecomputeIfDirty()
        {
            if (!dirty)
                return;

            float flatValue = 0f, percentValue = 0f;
            float flatRegen = 0f, percentRegen = 0f;

            for (int i = 0; i < modifiers.Count; i++)
            {
                StatModifier modifier = modifiers[i];
                if (modifier.Target == StatModifierTarget.Value)
                {
                    if (modifier.Type == StatModifierType.Flat)
                        flatValue += modifier.Value;
                    else
                        percentValue += modifier.Value;
                }
                else
                {
                    if (modifier.Type == StatModifierType.Flat)
                        flatRegen += modifier.Value;
                    else
                        percentRegen += modifier.Value;
                }
            }

            cachedValue = Mathf.Clamp(
                (baseValue + flatValue) * (1f + percentValue),
                Definition.MinValue, Definition.MaxValue);
            cachedRegen = (Definition.RegenPerSecond + flatRegen) * (1f + percentRegen);
            dirty = false;
        }
    }
}
