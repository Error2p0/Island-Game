namespace IslandGame.Creatures
{
    /// <summary>
    /// The base AI states of the creatures phase. Chase exists as a state and
    /// transition target already (Hostile detection, Neutral retaliation);
    /// the actual attack executed FROM Chase is the combat phase's addition.
    /// </summary>
    public enum CreatureAIState
    {
        /// <summary>Standing still, waiting out a short timer before wandering.</summary>
        Idle = 0,

        /// <summary>Walking to a random point within the home wander radius.</summary>
        Wander = 1,

        /// <summary>Player detected: stopped, facing them, deciding the aggression reaction.</summary>
        Alert = 2,

        /// <summary>Running directly away until beyond the safe distance (Passive reaction).</summary>
        Flee = 3,

        /// <summary>Closing distance to the player; hands over to Attack at approach range (Hostile/Neutral-retaliation).</summary>
        Chase = 4,

        /// <summary>One attack cycle: windup → timed hit window → recovery, then back to Chase to reposition (combat phase).</summary>
        Attack = 5,

        /// <summary>Tamed branch (taming phase): keeping station near the player; Assist mode engages from here.</summary>
        TamedFollow = 6,

        /// <summary>Tamed branch (taming phase): holding the commanded position.</summary>
        TamedStay = 7,
    }
}
