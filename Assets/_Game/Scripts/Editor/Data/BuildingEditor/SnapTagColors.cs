using System.Collections.Generic;
using IslandGame.Data.Building;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// One color per snap tag, shared by the Building Piece Editor's 3D
    /// socket gizmos and the inspector's swatches so a designer can match a
    /// gizmo to its list row at a glance. Standard tags get fixed, deliberate
    /// colors (mating pairs sit near each other in hue); unknown/custom tags
    /// get a stable hash-derived color so they are still distinguishable.
    /// </summary>
    internal static class SnapTagColors
    {
        private static readonly Dictionary<string, Color> Known = new Dictionary<string, Color>
        {
            { SnapTags.FoundationTop, new Color(0.95f, 0.75f, 0.20f) },  // gold
            { SnapTags.FoundationSide, new Color(0.75f, 0.50f, 0.15f) }, // brown-gold
            { SnapTags.WallBottom, new Color(0.30f, 0.85f, 0.35f) },     // green
            { SnapTags.WallTop, new Color(0.20f, 0.60f, 0.95f) },        // blue
            { SnapTags.WallSide, new Color(0.25f, 0.90f, 0.85f) },       // cyan
            { SnapTags.FloorEdge, new Color(0.90f, 0.45f, 0.85f) },      // magenta
            { SnapTags.RoofBottom, new Color(0.95f, 0.35f, 0.25f) },     // red
            { SnapTags.RoofTop, new Color(0.95f, 0.55f, 0.55f) },        // salmon
            { SnapTags.RoofSide, new Color(0.70f, 0.30f, 0.60f) },       // plum
            { SnapTags.Doorway, new Color(0.85f, 0.85f, 0.30f) },        // yellow-green
            { SnapTags.DoorHinge, new Color(0.60f, 0.85f, 0.30f) },      // lime
        };

        private static readonly Color Untagged = new Color(0.6f, 0.6f, 0.6f);

        public static Color Get(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return Untagged;

            if (Known.TryGetValue(tag, out Color color))
                return color;

            // Stable per-string hue so custom tags keep their color across
            // sessions and machines (string.GetHashCode is randomized —
            // derive from characters instead).
            int hash = 17;
            foreach (char c in tag)
                hash = hash * 31 + c;

            float hue = Mathf.Abs(hash % 360) / 360f;
            return Color.HSVToRGB(hue, 0.75f, 0.9f);
        }
    }
}
