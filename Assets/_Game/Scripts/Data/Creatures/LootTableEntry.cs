using System;
using IslandGame.Data.Items;
using UnityEngine;

namespace IslandGame.Data.Creatures
{
    /// <summary>
    /// One line of a creature's loot table: an item, a quantity range and a
    /// drop chance. Authored on CreatureDefinition now; ROLLED by the combat
    /// phase's death handling (this phase only fires OnDeath). Also the shape
    /// the structures phase reuses for chest loot.
    /// </summary>
    [Serializable]
    public sealed class LootTableEntry
    {
        public ItemDefinition item;

        [Min(0)]
        public int countMin = 1;

        [Min(1)]
        public int countMax = 1;

        [Tooltip("Probability this line drops at all (rolled once; the count range is rolled after).")]
        [Range(0f, 1f)]
        public float dropChance = 1f;
    }
}
