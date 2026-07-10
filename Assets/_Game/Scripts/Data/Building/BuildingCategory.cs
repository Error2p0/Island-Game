namespace IslandGame.Data.Building
{
    /// <summary>
    /// Coarse classification of a building piece, used by the build menu for
    /// grouping/filtering and by placement rules that differ per kind (e.g.
    /// foundations may touch terrain, walls may not float). Purely descriptive
    /// — snapping is driven entirely by socket tags, never by category.
    /// Append with the next unused value — never reorder, renumber or delete
    /// entries, assets serialize the numeric value.
    /// </summary>
    public enum BuildingCategory
    {
        Foundation = 0,
        Wall = 1,
        Floor = 2,
        Roof = 3,
        Door = 4,
        Window = 5,
        Stair = 6,
        Support = 7,
        Functional = 8,
        Decoration = 9,
    }
}
