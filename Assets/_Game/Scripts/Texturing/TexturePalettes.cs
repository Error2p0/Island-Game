using System.Collections.Generic;
using UnityEngine;

namespace IslandGame.Texturing
{
    /// <summary>One named base color for a style — "Oak", "Birch", "Granite"...</summary>
    public readonly struct TexturePreset
    {
        public readonly string Name;
        public readonly Color Color;

        public TexturePreset(string name, Color color)
        {
            Name = name;
            Color = color;
        }
    }

    /// <summary>
    /// The named palette-swap lists (requirement 3's convenient form): each
    /// style ships a handful of curated base colors so "birch planks" or
    /// "copper ore" is a dropdown pick, not per-variant code. The editor
    /// section surfaces these; the Phase 9 batch pass indexes them directly.
    /// Base color + hue-shift remain free-form — presets are starting points,
    /// not a restriction.
    /// </summary>
    public static class TexturePalettes
    {
        private static readonly Dictionary<TextureStyle, TexturePreset[]> Presets =
            new Dictionary<TextureStyle, TexturePreset[]>
            {
                [TextureStyle.Stone] = new[]
                {
                    new TexturePreset("Granite", new Color(0.55f, 0.55f, 0.57f)),
                    new TexturePreset("Basalt", new Color(0.34f, 0.34f, 0.38f)),
                    new TexturePreset("Sandstone", new Color(0.76f, 0.68f, 0.52f)),
                    new TexturePreset("Slate", new Color(0.42f, 0.46f, 0.52f)),
                },
                [TextureStyle.Wood] = new[]
                {
                    new TexturePreset("Oak", new Color(0.62f, 0.46f, 0.29f)),
                    new TexturePreset("Birch", new Color(0.83f, 0.76f, 0.62f)),
                    new TexturePreset("Pine", new Color(0.50f, 0.35f, 0.20f)),
                    new TexturePreset("Ebony", new Color(0.26f, 0.21f, 0.17f)),
                },
                [TextureStyle.Sand] = new[]
                {
                    new TexturePreset("Beach", new Color(0.87f, 0.80f, 0.60f)),
                    new TexturePreset("Desert", new Color(0.89f, 0.72f, 0.45f)),
                    new TexturePreset("Ash", new Color(0.45f, 0.43f, 0.42f)),
                },
                [TextureStyle.Grass] = new[]
                {
                    new TexturePreset("Meadow", new Color(0.36f, 0.60f, 0.26f)),
                    new TexturePreset("Dry", new Color(0.60f, 0.58f, 0.30f)),
                    new TexturePreset("Lush", new Color(0.23f, 0.54f, 0.28f)),
                },
                [TextureStyle.Metal] = new[]
                {
                    new TexturePreset("Iron", new Color(0.62f, 0.63f, 0.66f)),
                    new TexturePreset("Copper", new Color(0.78f, 0.50f, 0.34f)),
                    new TexturePreset("Gold", new Color(0.90f, 0.75f, 0.33f)),
                    new TexturePreset("Dark Steel", new Color(0.36f, 0.38f, 0.42f)),
                },
                [TextureStyle.Fabric] = new[]
                {
                    new TexturePreset("Linen", new Color(0.82f, 0.77f, 0.66f)),
                    new TexturePreset("Wool Red", new Color(0.62f, 0.25f, 0.22f)),
                    new TexturePreset("Wool Blue", new Color(0.27f, 0.37f, 0.60f)),
                },
                [TextureStyle.Foliage] = new[]
                {
                    new TexturePreset("Leaf", new Color(0.28f, 0.52f, 0.24f)),
                    new TexturePreset("Autumn", new Color(0.72f, 0.45f, 0.18f)),
                    new TexturePreset("Jungle", new Color(0.16f, 0.45f, 0.22f)),
                },
                [TextureStyle.Liquid] = new[]
                {
                    new TexturePreset("Water", new Color(0.25f, 0.45f, 0.70f)),
                    new TexturePreset("Lava", new Color(0.90f, 0.36f, 0.10f)),
                    new TexturePreset("Swamp", new Color(0.30f, 0.42f, 0.30f)),
                },
            };

        private static readonly TexturePreset[] Empty = new TexturePreset[0];

        public static IReadOnlyList<TexturePreset> GetPresets(TextureStyle style)
        {
            return Presets.TryGetValue(style, out TexturePreset[] presets) ? presets : Empty;
        }
    }
}
