namespace IslandGame.Creatures
{
    /// <summary>
    /// What a tamed companion is currently doing (taming phase). Cycled by
    /// interacting with the companion (Follow → Stay → Assist-if-capable →
    /// Follow) and persisted by name in the save file — append new modes,
    /// never reorder.
    /// </summary>
    public enum CompanionMode
    {
        /// <summary>Stays near the player, catching up (and teleporting when hopelessly far behind).</summary>
        Follow = 0,

        /// <summary>Holds its position until commanded otherwise.</summary>
        Stay = 1,

        /// <summary>Follow, plus engages hostile creatures the player is fighting (combat-capable species only).</summary>
        Assist = 2,
    }
}
