namespace IslandGame.Data.Items
{
    /// <summary>
    /// Broad gameplay classification of an item. Drives filtering in the
    /// creative menu, editor tools and crafting UI.
    ///
    /// EXTENSION RULE: assets serialize the numeric value, so append new
    /// categories at the end with the next number — never reorder, renumber or
    /// remove entries, or every authored item silently changes category.
    /// </summary>
    public enum ItemCategory
    {
        Resource = 0,
        Tool = 1,
        Weapon = 2,
        Consumable = 3,
        Block = 4,
        Placeable = 5,
        Misc = 6,
    }
}
