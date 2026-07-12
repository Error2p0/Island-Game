namespace IslandGame.Data.World
{
    /// <summary>
    /// What kind of ground a structure template wants. Drives the placement
    /// system's site validation against the generator's height data.
    /// </summary>
    public enum StructureSurface
    {
        /// <summary>Flat grassy land above the beach band (towers, camps).</summary>
        Inland = 0,

        /// <summary>The land/water boundary: a sandy shore column with open water off one side (docks, wrecks). Placement picks the yaw that points seaward.</summary>
        Coast = 1,
    }
}
