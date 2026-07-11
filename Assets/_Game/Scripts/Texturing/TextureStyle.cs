namespace IslandGame.Texturing
{
    /// <summary>
    /// Material families the texture generator knows how to draw. Each style
    /// is a DISTINCT pixel algorithm in TextureSynth (grain vs mottle vs
    /// weave...), not one noise function recolored. Append-only — generated
    /// asset filenames and Phase 9 batch recipes reference these by name.
    /// </summary>
    public enum TextureStyle
    {
        Stone = 0,
        Wood = 1,
        Sand = 2,
        Grass = 3,
        Metal = 4,
        Fabric = 5,
        Foliage = 6,
        Liquid = 7,
    }

    /// <summary>
    /// Icon silhouettes for item sprites — a deliberately small,
    /// category-appropriate set (tools, weapons, liquids, generic goods).
    /// Raw material noise doesn't read at 24 px; a clean two-tone shape with
    /// an outline does. Append-only for the same reason as TextureStyle.
    /// </summary>
    public enum IconShape
    {
        RoundedSquare = 0,
        Circle = 1,
        Pickaxe = 2,
        Axe = 3,
        Sword = 4,
        Droplet = 5,
        Ingot = 6,
    }
}
