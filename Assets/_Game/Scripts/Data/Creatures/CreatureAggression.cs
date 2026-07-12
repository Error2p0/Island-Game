namespace IslandGame.Data.Creatures
{
    /// <summary>
    /// How a creature relates to the player, driving the AI's transition
    /// table: Passive creatures flee proximity and attacks, Neutral creatures
    /// ignore the player until attacked, Hostile creatures actively detect
    /// and close distance (attacking lands in the combat phase).
    /// </summary>
    public enum CreatureAggression
    {
        Passive = 0,
        Neutral = 1,
        Hostile = 2,
    }
}
