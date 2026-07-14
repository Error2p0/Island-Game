namespace IslandGame.Data.Foliage
{
    /// <summary>
    /// Broad gameplay class of a foliage piece. Systems branch on this, not on
    /// IDs: the scatter system treats every category the same, but future
    /// phases key behavior off it (ambient wind/rustle for Shrub, bee spawns
    /// for Flower, throwable pebbles for RockClutter). Extend by appending —
    /// values are serialized into FoliageDefinition assets.
    /// </summary>
    public enum FoliageCategory
    {
        /// <summary>Waist-high woody plant — the usual harvestable (berries, fiber).</summary>
        Bush = 0,

        /// <summary>Purely ambient greenery. Usually no yield; decoration the world scatters for density.</summary>
        Shrub = 1,

        /// <summary>Small ground flower/plant. Future herbalism/bee content hangs here.</summary>
        Flower = 2,

        /// <summary>Loose stones, driftwood and similar ground clutter.</summary>
        RockClutter = 3,
    }
}
