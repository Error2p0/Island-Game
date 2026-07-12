namespace IslandGame.Data.Stats
{
    /// <summary>
    /// How a stat's current value behaves at runtime. The split exists because
    /// "health" and "mining speed" want opposite rules from the same modifier
    /// math: both compute a modified value from base + modifiers, but only one
    /// of them is a spendable pool.
    /// </summary>
    public enum StatKind
    {
        /// <summary>
        /// A spendable pool (health, stamina, hunger): Current moves between
        /// the min clamp and the MODIFIED value (which acts as the maximum),
        /// depleted by damage/use and refilled by regen. Modifiers change the
        /// maximum, not the current amount.
        /// </summary>
        Resource = 0,

        /// <summary>
        /// A derived number (mining speed, carry capacity): Current always
        /// EQUALS the modified value. There is nothing to spend or regenerate —
        /// buffs/debuffs move the value directly.
        /// </summary>
        Attribute = 1,
    }
}
