using System;
using IslandGame.Data.Stats;
using UnityEngine;

namespace IslandGame.Data.Creatures
{
    /// <summary>
    /// One stat a creature species has: a shared StatDefinition reference
    /// plus this species' base value (a deer and a boar share ONE "health"
    /// StatDefinition asset but differ in base value here — no per-species
    /// stat assets). 0 means "use the StatDefinition's own Base Value".
    /// </summary>
    [Serializable]
    public struct CreatureStatEntry
    {
        public StatDefinition stat;

        [Tooltip("This species' base value for the stat. 0 = use the StatDefinition's authored Base Value.")]
        [Min(0f)]
        public float baseValue;
    }
}
