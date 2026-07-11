using System;
using IslandGame.Data.Items;
using IslandGame.Texturing;
using UnityEditor;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// The "Auto-Generate" UI block shared by the Block Editor (material
    /// textures) and Item Editor (icons): style/shape pickers, palette
    /// presets, base color + hue shift + seed, a LIVE point-filtered preview
    /// that re-renders on any parameter change, and a Generate button.
    ///
    /// Two-phase commit, matching the editors' Save/Revert model as closely
    /// as a disk asset allows: Generate writes/overwrites the .png asset
    /// immediately (gen_&lt;id&gt;.png — a stable path, so re-rolls never
    /// dangle references), but the ASSIGNMENT to the block/item lands in the
    /// unapplied session buffer like any other field edit — Revert leaves
    /// the definition untouched. The UI says so.
    ///
    /// One instance per inspector panel; the owning window must Dispose it
    /// (the preview texture is an unmanaged GPU object).
    /// </summary>
    internal sealed class TextureGeneratorSection : IDisposable
    {
        private static readonly int[] BlockResolutions = { 16, 32 };
        private static readonly string[] BlockResolutionLabels = { "16 × 16 (project default)", "32 × 32" };

        // Block-texture parameters.
        private TextureStyle style = TextureStyle.Stone;
        private Color baseColor = new Color(0.55f, 0.55f, 0.57f);
        private float hueShift;
        private int seed = 1;
        private int resolutionIndex;

        // Icon parameters.
        private IconShape iconShape = IconShape.RoundedSquare;
        private Color iconPrimary = new Color(0.62f, 0.63f, 0.66f);
        private Color iconSecondary = new Color(0.5f, 0.36f, 0.22f);

        private Texture2D preview;
        private bool previewDirty = true;
        private bool previewIsIcon;

        public void Dispose()
        {
            if (preview != null)
            {
                UnityEngine.Object.DestroyImmediate(preview);
                preview = null;
            }
        }

        // ------------------------------------------------------------------
        // Block Editor: material texture generation
        // ------------------------------------------------------------------

        public void DrawBlockSection(BlockEditSession session)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Auto-Generate Texture", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();

            style = (TextureStyle)EditorGUILayout.EnumPopup("Style", style);
            DrawPresetRow();
            baseColor = EditorGUILayout.ColorField("Base Color", baseColor);
            hueShift = EditorGUILayout.Slider(new GUIContent("Hue Shift", "Cheap variation without picking a new color."), hueShift, -0.5f, 0.5f);
            DrawSeedRow();
            resolutionIndex = EditorGUILayout.Popup("Resolution", resolutionIndex, BlockResolutionLabels);

            if (EditorGUI.EndChangeCheck())
                previewDirty = true;

            int size = BlockResolutions[resolutionIndex];
            DrawPreview(isIcon: false, size);

            string id = session.CurrentId != null ? session.CurrentId.Trim() : string.Empty;
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(id)))
            {
                if (GUILayout.Button("Generate & Assign To Uniform Texture", GUILayout.Height(24f)))
                {
                    string path = $"{GeneratedTextureAssets.BlockTextureFolder}/gen_{id}.png";
                    Color32[] pixels = TextureSynth.GeneratePixels(style, baseColor, size, seed, hueShift);
                    Texture2D asset = GeneratedTextureAssets.WriteBlockTexture(path, pixels, size);

                    session.Serialized.FindProperty("useUniformTexture").boolValue = true;
                    session.Serialized.FindProperty("uniformTexture").objectReferenceValue = asset;
                }
            }

            EditorGUILayout.HelpBox(
                string.IsNullOrEmpty(id)
                    ? "Set an ID first — the generated file is named after it (gen_<id>.png)."
                    : $"Writes {GeneratedTextureAssets.BlockTextureFolder}/gen_{id}.png to disk immediately " +
                      "(re-rolls overwrite the same file); the assignment above still follows Save/Revert.",
                MessageType.None);
        }

        // ------------------------------------------------------------------
        // Item Editor: icon generation
        // ------------------------------------------------------------------

        public void DrawIconSection(ItemEditSession session)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Auto-Generate Icon", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();

            using (new EditorGUILayout.HorizontalScope())
            {
                iconShape = (IconShape)EditorGUILayout.EnumPopup("Shape", iconShape);
                if (GUILayout.Button(new GUIContent("Match Category", "Pick the silhouette suggested by the item's Category."),
                        EditorStyles.miniButton, GUILayout.Width(110f)))
                {
                    var category = (ItemCategory)session.Serialized.FindProperty("category").intValue;
                    iconShape = SuggestShape(category);
                }
            }

            iconPrimary = EditorGUILayout.ColorField(new GUIContent("Primary", "Head/body fill."), iconPrimary);
            iconSecondary = EditorGUILayout.ColorField(new GUIContent("Secondary", "Handle/grip fill (tools, weapons)."), iconSecondary);
            DrawSeedRow();

            if (EditorGUI.EndChangeCheck())
                previewDirty = true;

            DrawPreview(isIcon: true, TextureSynth.DefaultIconSize);

            string id = session.CurrentId != null ? session.CurrentId.Trim() : string.Empty;
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(id)))
            {
                if (GUILayout.Button("Generate & Assign Icon", GUILayout.Height(24f)))
                {
                    string path = $"{GeneratedTextureAssets.IconFolder}/gen_icon_{id}.png";
                    Color32[] pixels = TextureSynth.GenerateIconPixels(
                        iconShape, iconPrimary, iconSecondary, TextureSynth.DefaultIconSize, seed);
                    Sprite sprite = GeneratedTextureAssets.WriteIconSprite(path, pixels, TextureSynth.DefaultIconSize);

                    session.Serialized.FindProperty("icon").objectReferenceValue = sprite;
                }
            }

            EditorGUILayout.HelpBox(
                string.IsNullOrEmpty(id)
                    ? "Set an ID first — the generated file is named after it (gen_icon_<id>.png)."
                    : $"Writes {GeneratedTextureAssets.IconFolder}/gen_icon_{id}.png to disk immediately; " +
                      "the icon assignment still follows Save/Revert.",
                MessageType.None);
        }

        /// <summary>Category → silhouette mapping (requirement 5's "category-appropriate").</summary>
        public static IconShape SuggestShape(ItemCategory category)
        {
            switch (category)
            {
                case ItemCategory.Tool: return IconShape.Pickaxe;
                case ItemCategory.Weapon: return IconShape.Sword;
                case ItemCategory.Consumable: return IconShape.Droplet;
                case ItemCategory.Misc: return IconShape.Circle;
                default: return IconShape.RoundedSquare; // Resource, Block, Placeable
            }
        }

        // ------------------------------------------------------------------
        // Shared rows
        // ------------------------------------------------------------------

        private void DrawPresetRow()
        {
            var presets = TexturePalettes.GetPresets(style);
            if (presets.Count == 0)
                return;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(new GUIContent("Palette Preset", "Curated base colors for this style — a starting point, not a restriction."));

                foreach (TexturePreset preset in presets)
                {
                    if (GUILayout.Button(preset.Name, EditorStyles.miniButton))
                    {
                        baseColor = preset.Color;
                        previewDirty = true;
                        GUI.FocusControl(null); // color field must not hold a stale value
                    }
                }
            }
        }

        private void DrawSeedRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                seed = EditorGUILayout.IntField(new GUIContent("Seed", "Same parameters + same seed = the same pixels, forever."), seed);
                if (GUILayout.Button("Randomize", EditorStyles.miniButton, GUILayout.Width(80f)))
                {
                    seed = UnityEngine.Random.Range(1, 100000);
                    previewDirty = true;
                }
            }
        }

        private void DrawPreview(bool isIcon, int size)
        {
            if (previewDirty || preview == null || previewIsIcon != isIcon)
            {
                Dispose();
                preview = isIcon
                    ? TextureSynth.GenerateIconTexture(iconShape, iconPrimary, iconSecondary, size, seed)
                    : TextureSynth.GenerateTexture(style, baseColor, size, seed, hueShift);
                preview.hideFlags = HideFlags.HideAndDontSave;
                previewDirty = false;
                previewIsIcon = isIcon;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                Rect rect = GUILayoutUtility.GetRect(72f, 72f, GUILayout.Width(72f), GUILayout.Height(72f));
                EditorGUI.DrawRect(new Rect(rect.x - 1, rect.y - 1, rect.width + 2, rect.height + 2), new Color(0f, 0f, 0f, 0.4f));
                EditorGUI.DrawTextureTransparent(rect, preview, ScaleMode.ScaleToFit);
                GUILayout.FlexibleSpace();
            }
        }
    }
}
