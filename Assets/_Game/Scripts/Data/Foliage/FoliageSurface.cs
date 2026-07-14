namespace IslandGame.Data.Foliage
{
    /// <summary>
    /// Where the scatter system may plant a foliage piece, expressed against
    /// the island generator's existing surface bands (the same sea-level /
    /// beach-band rules trees and structures already use — no new biome data).
    /// </summary>
    public enum FoliageSurface
    {
        /// <summary>Grassy land above the beach band — the tree rule (berry bushes, shrubs).</summary>
        Grass = 0,

        /// <summary>The shoreline band: sand columns around sea level, including ankle-deep water (reeds).</summary>
        Shore = 1,
    }
}
