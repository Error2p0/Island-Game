namespace IslandGame.Data.Crafting
{
    /// <summary>
    /// Which crafting station a recipe demands. None = craftable from the
    /// hand menu anywhere. Stations in the world are any collider carrying a
    /// CraftingStationMarker with the matching type (see that class for the
    /// convention). Append only — never reorder or renumber, recipes
    /// serialize the value.
    /// </summary>
    public enum CraftingStationType
    {
        None = 0,
        Workbench = 1,
        Forge = 2,
        Campfire = 3,

        /// <summary>Smelting station (ore → bars). Forge above stays reserved for a future anvil/smithing tier.</summary>
        Furnace = 4,
    }
}
