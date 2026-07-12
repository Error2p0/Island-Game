using System;
using System.Collections.Generic;
using IslandGame.Data.Stats;
using UnityEngine;

namespace IslandGame.Stats
{
    /// <summary>
    /// The generic runtime attribute component: holds a StatInstance for each
    /// StatDefinition in its serialized list, ticks regen/decay and timed
    /// modifiers, and raises the container-level events. Deliberately NOT a
    /// player script — creatures in the AI phase declare their own (smaller)
    /// stat lists on the same component, and the combat/damage code only ever
    /// talks to this class.
    ///
    /// EVENTS are generic by stat ID (never hardcoded to health/stamina):
    ///   OnStatChanged(statId, oldValue, newValue) — every actual change,
    ///     including regen ticks (so UIs subscribe instead of polling).
    ///   OnStatDepleted(statId) — edge: value reached its min clamp.
    ///   OnStatFull(statId)     — edge: a Resource reached its modified max.
    ///
    /// PERFORMANCE: Update is O(stats) with no allocations — one branch per
    /// idle stat, timed-modifier scans only on stats that HAVE timed modifiers.
    /// Dozens of creatures with containers cost microseconds, which is why
    /// regen lives here centrally instead of in per-feature Update loops.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StatContainer : MonoBehaviour
    {
        [Tooltip("The stats this entity has. Player and creatures each list only what's relevant to them; order is irrelevant, duplicates are ignored with a warning.")]
        [SerializeField] private List<StatDefinition> stats = new List<StatDefinition>();

        private readonly List<StatInstance> instances = new List<StatInstance>();
        private readonly Dictionary<string, StatInstance> byId = new Dictionary<string, StatInstance>();
        private bool built;

        /// <summary>Raised on every actual value change: (statId, oldValue, newValue).</summary>
        public event Action<string, float, float> OnStatChanged;

        /// <summary>Raised once when a stat hits its min clamp (health 0 = death, stamina 0 = exhausted).</summary>
        public event Action<string> OnStatDepleted;

        /// <summary>Raised once when a Resource stat refills to its modified maximum.</summary>
        public event Action<string> OnStatFull;

        /// <summary>Every stat on this entity (HUD binding, debugging).</summary>
        public IReadOnlyList<StatInstance> Instances
        {
            get
            {
                EnsureBuilt();
                return instances;
            }
        }

        private void Awake()
        {
            EnsureBuilt();
        }

        /// <summary>
        /// Builds instances on first use. Called from Awake AND from every
        /// public entry point: Awake order across components is undefined, so
        /// consumers (locomotion, UI) may legally query before our Awake ran —
        /// same defensive pattern as InventorySystem.EnsureSlots.
        /// </summary>
        private void EnsureBuilt()
        {
            if (built)
                return;

            built = true;
            for (int i = 0; i < stats.Count; i++)
            {
                StatDefinition definition = stats[i];
                if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
                    continue;

                if (byId.ContainsKey(definition.Id))
                {
                    Debug.LogWarning($"[StatContainer] Duplicate stat '{definition.Id}' on '{name}' ignored.", this);
                    continue;
                }

                var instance = new StatInstance(definition);
                instances.Add(instance);
                byId.Add(definition.Id, instance);
            }
        }

        /// <summary>
        /// Runtime (re)configuration for spawned entities: replaces whatever
        /// the serialized list built with the given definitions and per-owner
        /// base-value overrides (0 or less = use the definition's default).
        /// Creature spawning calls this on Init — including pooled REUSE,
        /// where the rebuild doubles as a full reset (fresh instances, no
        /// leftover modifiers, resources at max). Event subscribers persist;
        /// they are on the container, not the instances.
        /// </summary>
        public void ConfigureStats(IReadOnlyList<StatDefinition> definitions, IReadOnlyList<float> baseValueOverrides = null)
        {
            built = true;
            instances.Clear();
            byId.Clear();

            for (int i = 0; i < definitions.Count; i++)
            {
                StatDefinition definition = definitions[i];
                if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
                    continue;

                if (byId.ContainsKey(definition.Id))
                {
                    Debug.LogWarning($"[StatContainer] Duplicate stat '{definition.Id}' on '{name}' ignored.", this);
                    continue;
                }

                float overrideValue =
                    baseValueOverrides != null && i < baseValueOverrides.Count && baseValueOverrides[i] > 0f
                        ? baseValueOverrides[i]
                        : float.NaN;

                var instance = new StatInstance(definition, overrideValue);
                instances.Add(instance);
                byId.Add(definition.Id, instance);
            }
        }

        private void Update()
        {
            float now = Time.time;
            float deltaTime = Time.deltaTime;

            for (int i = 0; i < instances.Count; i++)
            {
                StatInstance instance = instances[i];

                // Timed buffs/debuffs expire here so no feature needs its own timer.
                if (instance.HasTemporaryModifiers)
                {
                    instance.RemoveExpired(now, out float expiredOld, out float expiredNew, out bool expiredChanged);
                    if (expiredChanged)
                        RaiseChanged(instance, expiredOld, expiredNew);
                }

                if (instance.Tick(deltaTime, now, out float oldValue, out float newValue))
                    RaiseChanged(instance, oldValue, newValue);
            }
        }

        // ------------------------------------------------------------------
        // Queries
        // ------------------------------------------------------------------

        public bool TryGetInstance(string statId, out StatInstance instance)
        {
            EnsureBuilt();
            return byId.TryGetValue(statId ?? string.Empty, out instance);
        }

        public bool Has(string statId)
        {
            return TryGetInstance(statId, out _);
        }

        /// <summary>Current value, or the fallback when this entity doesn't have the stat — so integrations degrade gracefully.</summary>
        public float GetValue(string statId, float fallback = 0f)
        {
            return TryGetInstance(statId, out StatInstance instance) ? instance.Current : fallback;
        }

        /// <summary>Modified maximum (Resource) / modified value (Attribute), or the fallback.</summary>
        public float GetModifiedValue(string statId, float fallback = 0f)
        {
            return TryGetInstance(statId, out StatInstance instance) ? instance.ModifiedValue : fallback;
        }

        /// <summary>0..1 between min clamp and modified maximum, or the fallback.</summary>
        public float GetNormalized(string statId, float fallback = 0f)
        {
            return TryGetInstance(statId, out StatInstance instance) ? instance.Normalized : fallback;
        }

        // ------------------------------------------------------------------
        // Mutations
        // ------------------------------------------------------------------

        /// <summary>
        /// Adjusts a Resource stat's current value by a delta (damage, meals,
        /// stamina spend). Returns the change actually applied after clamping.
        /// </summary>
        public float Modify(string statId, float delta)
        {
            if (!TryGetInstance(statId, out StatInstance instance))
                return 0f;

            if (!instance.ApplyDelta(delta, Time.time, out float oldValue, out float newValue))
                return 0f;

            RaiseChanged(instance, oldValue, newValue);
            return newValue - oldValue;
        }

        /// <summary>Sets a Resource stat's current value directly (respawn refill, save/load). Clamped.</summary>
        public void SetCurrent(string statId, float value)
        {
            if (!TryGetInstance(statId, out StatInstance instance))
                return;

            if (instance.SetCurrent(value, Time.time, out float oldValue, out float newValue))
                RaiseChanged(instance, oldValue, newValue);
        }

        /// <summary>Refills a Resource stat to its modified maximum.</summary>
        public void RefillToMax(string statId)
        {
            if (TryGetInstance(statId, out StatInstance instance))
                SetCurrent(statId, instance.ModifiedValue);
        }

        /// <summary>Applies a modifier to a stat. Timed modifiers expire automatically.</summary>
        public bool AddModifier(string statId, StatModifier modifier)
        {
            if (modifier == null || !TryGetInstance(statId, out StatInstance instance))
                return false;

            if (instance.AddModifier(modifier, Time.time, out float oldValue, out float newValue))
                RaiseChanged(instance, oldValue, newValue);
            return true;
        }

        /// <summary>Removes one specific modifier instance (the caller kept the reference from AddModifier).</summary>
        public bool RemoveModifier(string statId, StatModifier modifier)
        {
            if (modifier == null || !TryGetInstance(statId, out StatInstance instance))
                return false;

            bool changed = instance.RemoveModifier(modifier, out float oldValue, out float newValue);
            if (changed)
                RaiseChanged(instance, oldValue, newValue);
            return true;
        }

        /// <summary>
        /// Removes every modifier the given source applied, across all stats
        /// (unequip, buff cleanup, feature teardown). Returns removed count.
        /// </summary>
        public int RemoveAllFromSource(object source)
        {
            EnsureBuilt();
            int removed = 0;

            for (int i = 0; i < instances.Count; i++)
            {
                StatInstance instance = instances[i];
                removed += instance.RemoveAllFromSource(
                    source, out float oldValue, out float newValue, out bool changed);
                if (changed)
                    RaiseChanged(instance, oldValue, newValue);
            }

            return removed;
        }

        // ------------------------------------------------------------------
        // Events
        // ------------------------------------------------------------------

        private void RaiseChanged(StatInstance instance, float oldValue, float newValue)
        {
            string id = instance.Definition.Id;
            OnStatChanged?.Invoke(id, oldValue, newValue);

            instance.UpdateEdgeFlags(out bool becameDepleted, out bool becameFull);
            if (becameDepleted)
                OnStatDepleted?.Invoke(id);
            if (becameFull)
                OnStatFull?.Invoke(id);
        }
    }
}
