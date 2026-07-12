namespace IslandGame.Data.Stats
{
    /// <summary>
    /// How a modifier's value combines into a stat. The formula, applied by
    /// StatInstance in one place:
    ///
    ///     result = (base + Σ Flat values) * (1 + Σ PercentMultiplier values)
    ///
    /// so PercentMultiplier 0.5 = +50%, -0.25 = -25%. Percent modifiers are
    /// summed before multiplying (two +50% = +100%, not +125%) — the common
    /// convention in the reference games, and the one that keeps stacking
    /// intuitive for designers.
    /// </summary>
    public enum StatModifierType
    {
        /// <summary>Added to the base value before percentages apply.</summary>
        Flat = 0,

        /// <summary>Fractional bonus summed with other percentages, then multiplied once.</summary>
        PercentMultiplier = 1,
    }
}
