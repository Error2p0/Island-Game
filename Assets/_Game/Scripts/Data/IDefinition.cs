namespace IslandGame.Data
{
    /// <summary>
    /// Contract shared by every authored definition asset (items, blocks, later
    /// recipes). The Id is the stable serialization key: terrain saves, inventory
    /// saves and recipes store this string — never an asset reference index or an
    /// array position, both of which break when content is added or reordered.
    /// Once content has been saved with an Id, that Id must never change.
    /// IDs are unique per definition type (item "stone" and block "stone" may
    /// coexist — they live in separate databases).
    /// </summary>
    public interface IDefinition
    {
        /// <summary>Stable unique ID, lowercase_underscore by convention (e.g. "wood_plank").</summary>
        string Id { get; }

        /// <summary>Human-readable name shown in UI.</summary>
        string DisplayName { get; }
    }
}
