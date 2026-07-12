using System;
using UnityEngine;

namespace IslandGame.Data.Stats
{
    /// <summary>
    /// One authored stat modifier an item grants WHILE EQUIPPED (a pickaxe's
    /// mining-speed bonus, a future backpack's carry capacity). Serialized on
    /// ItemDefinition; EquippedItemStatModifiers converts these into runtime
    /// StatModifiers on equip and removes them on unequip. References the stat
    /// by stable ID — same convention as every other cross-asset link.
    /// </summary>
    [Serializable]
    public struct EquipStatModifier
    {
        [Tooltip("Stable ID of the stat to modify (e.g. mining_speed, carry_capacity).")]
        public string statId;

        [Tooltip("Value = the stat's modified value/maximum; RegenRate = its per-second regen.")]
        public StatModifierTarget target;

        [Tooltip("Flat is added to base; PercentMultiplier is a fraction (0.5 = +50%, -0.25 = -25%).")]
        public StatModifierType type;

        [Tooltip("Modifier amount, interpreted per Type.")]
        public float value;
    }
}
