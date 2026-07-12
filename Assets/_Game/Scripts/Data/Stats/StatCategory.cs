namespace IslandGame.Data.Stats
{
    /// <summary>
    /// UI/design grouping for stats (HUD sections, future character screens).
    /// Purely organizational — no runtime logic branches on it. Extend freely;
    /// values are serialized by name-independent int, so append new entries at
    /// the end and never reorder existing ones.
    /// </summary>
    public enum StatCategory
    {
        /// <summary>Core life resources: health, stamina.</summary>
        Vital = 0,

        /// <summary>Environmental/needs stats: hunger, thirst, warmth.</summary>
        Survival = 1,

        /// <summary>Offense/defense stats (armor, damage bonuses — future phases).</summary>
        Combat = 2,

        /// <summary>Everything else: mining speed, carry capacity, crafting bonuses.</summary>
        Utility = 3,
    }
}
