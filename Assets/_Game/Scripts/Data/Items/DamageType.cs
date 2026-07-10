namespace IslandGame.Data.Items
{
    /// <summary>
    /// How a weapon's hits are classified — armor/resistance rules and hit
    /// feedback read this. Same extension rule as all data enums: append with
    /// the next number, never reorder or renumber (assets serialize the value).
    /// </summary>
    public enum DamageType
    {
        Blunt = 0,
        Slash = 1,
        Pierce = 2,
    }
}
