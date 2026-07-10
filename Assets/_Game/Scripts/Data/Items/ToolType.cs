namespace IslandGame.Data.Items
{
    /// <summary>
    /// What kind of tool an item is. Pairs with ItemDefinition.EfficientBlocks
    /// to describe what the tool is good at; permission to mine at all is the
    /// tier check (ItemDefinition.ToolTier vs BlockDefinition.RequiredToolTier).
    /// Append only — never reorder or renumber.
    /// </summary>
    public enum ToolType
    {
        /// <summary>Not a typed tool (or Is Tool is off).</summary>
        None = 0,
        Axe = 1,
        Pickaxe = 2,
        Shovel = 3,
        Hoe = 4,
    }
}
