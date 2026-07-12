namespace IslandGame.Data.Stats
{
    /// <summary>
    /// Which number on the stat a modifier affects. Splitting the target keeps
    /// ONE modifier system covering both "bigger stamina bar" (Value) and
    /// "stamina recovers slower while starving" (RegenRate) — no feature ever
    /// needs to hack stat fields directly.
    /// </summary>
    public enum StatModifierTarget
    {
        /// <summary>
        /// The stat's modified value — the maximum for Resource stats, the
        /// value itself for Attribute stats.
        /// </summary>
        Value = 0,

        /// <summary>
        /// The regen/decay rate (units per second). Negative results drain the
        /// stat; the regen delay only gates POSITIVE net regen.
        /// </summary>
        RegenRate = 1,
    }
}
