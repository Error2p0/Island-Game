namespace IslandGame.Data.Stats
{
    /// <summary>
    /// The stat IDs runtime code wires against, so integrations (locomotion's
    /// stamina, mining's speed multiplier, the inventory's carry capacity)
    /// never scatter magic strings. These must match the Id fields of the
    /// assets the stat content creator generates. Content-only stats that no
    /// code reads by name don't need an entry here.
    /// </summary>
    public static class StatIds
    {
        public const string Health = "health";
        public const string Stamina = "stamina";
        public const string Hunger = "hunger";
        public const string Thirst = "thirst";
        public const string Warmth = "warmth";
        public const string MiningSpeed = "mining_speed";
        public const string CarryCapacity = "carry_capacity";

        // Creature-centric stats (creatures phase). The player's container
        // deliberately does NOT carry these — see StatsSystemBuilder's
        // explicit player stat set.
        public const string MoveSpeed = "move_speed";
        public const string AttackDamage = "attack_damage";
        public const string DetectionRadius = "detection_radius";
    }
}
