using System.Linq;
using IslandGame.Data.Blocks;
using IslandGame.Data.Building;
using IslandGame.Data.Items;
using IslandGame.Data.World;
using IslandGame.Texturing;
using UnityEditor;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// THE Phase 9 batch content pass (Island Game → Data → Generate Base
    /// Content Set): programmatically creates the full playable content set —
    /// blocks, items, tools/weapons/food, building pieces with prefabs and
    /// behaviors, and every recipe — through the same APIs the individual
    /// editors use, so the set is reviewable as code and rerunnable.
    ///
    /// IDEMPOTENCE CONTRACT (same as every content creator, with one
    /// deliberate difference): DEFINITION assets are create-if-missing —
    /// hand edits survive reruns. TEXTURES regenerate every run (that is the
    /// point of a procedural pipeline: tweak TextureSynth, rerun, everything
    /// updates — and generation is seeded per-ID, so untouched code produces
    /// byte-identical files). A few TARGETED MIGRATIONS upgrade earlier
    /// example content in place (documented at the call sites), each guarded
    /// so an intentionally-authored value is never clobbered.
    ///
    /// Split across ContentSetItemsAndBlocks / ContentSetPlaceables /
    /// ContentSetRecipes; this file owns orchestration, counters and the
    /// shared helpers they all call.
    /// </summary>
    public static class BaseContentSetGenerator
    {
        public const string BlockFolder = "Assets/_Game/Content/Blocks";
        public const string ItemFolder = "Assets/_Game/Content/Items";
        public const string RecipeFolder = "Assets/_Game/Content/Recipes";
        public const string PieceFolder = "Assets/_Game/Content/Building/Pieces";
        public const string PrefabFolder = "Assets/_Game/Content/Building/Prefabs";
        public const string MaterialFolder = "Assets/_Game/Content/Building/Materials";

        // Run counters — CREATED only (loaded-existing doesn't count).
        public static int BlocksCreated;
        public static int ItemsCreated;
        public static int PiecesCreated;
        public static int RecipesCreated;
        public static int TexturesWritten;
        public static int PrefabsCreated;

        [MenuItem("Island Game/Data/Generate Base Content Set")]
        public static void Run()
        {
            BlocksCreated = ItemsCreated = PiecesCreated = RecipesCreated = TexturesWritten = PrefabsCreated = 0;

            DefinitionDatabaseSync.EnsureFolderExists(BlockFolder);
            DefinitionDatabaseSync.EnsureFolderExists(ItemFolder);
            DefinitionDatabaseSync.EnsureFolderExists(RecipeFolder);
            DefinitionDatabaseSync.EnsureFolderExists(PieceFolder);
            DefinitionDatabaseSync.EnsureFolderExists(PrefabFolder);
            DefinitionDatabaseSync.EnsureFolderExists(MaterialFolder);
            DefinitionDatabaseSync.EnsureFolderExists(GeneratedTextureAssets.BlockTextureFolder);
            DefinitionDatabaseSync.EnsureFolderExists(GeneratedTextureAssets.IconFolder);

            try
            {
                // Order matters: items exist before blocks link drops, blocks
                // before placer-items link PlacedBlock, everything before recipes.
                EditorUtility.DisplayProgressBar("Generate Base Content Set", "Resource items…", 0.05f);
                ContentSetItemsAndBlocks.CreateResourceItems();
                EditorUtility.DisplayProgressBar("Generate Base Content Set", "Blocks…", 0.2f);
                ContentSetItemsAndBlocks.CreateBlocks();
                EditorUtility.DisplayProgressBar("Generate Base Content Set", "Placer items, tools, weapons, food…", 0.4f);
                ContentSetItemsAndBlocks.CreateBlockPlacerItems();
                ContentSetItemsAndBlocks.CreateToolsAndWeapons();
                ContentSetItemsAndBlocks.CreateFoodAndMisc();
                EditorUtility.DisplayProgressBar("Generate Base Content Set", "Placeables…", 0.6f);
                ContentSetPlaceables.Create();
                EditorUtility.DisplayProgressBar("Generate Base Content Set", "Recipes…", 0.8f);
                ContentSetRecipes.Create();
                EditorUtility.DisplayProgressBar("Generate Base Content Set", "Migrations + sync…", 0.9f);
                ApplyMigrations();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            DefinitionDatabaseSync.SyncAll();

            Debug.Log(
                $"[BaseContentSet] Done. Created this run: {BlocksCreated} blocks, {ItemsCreated} items, " +
                $"{PiecesCreated} building pieces, {RecipesCreated} recipes, {PrefabsCreated} prefabs; " +
                $"{TexturesWritten} textures (re)generated. Existing definitions were left untouched — " +
                "rerun any time after tweaking the generators.");
        }

        // ------------------------------------------------------------------
        // Targeted migrations of earlier example content (each guarded)
        // ------------------------------------------------------------------

        private static void ApplyMigrations()
        {
            // Tier ladder rebase: wood tools are tier 1 (mine stone), stone
            // tier 2 (mine copper/tin ore), copper tier 3 (mine silver). The
            // example Stone Pickaxe predates the ladder at tier 1 — lift it
            // only if it still carries that original value.
            ItemDefinition stonePickaxe = FindItem("stone_pickaxe");
            if (stonePickaxe != null && stonePickaxe.ToolTier == 1)
            {
                var serialized = new SerializedObject(stonePickaxe);
                serialized.FindProperty("toolTier").intValue = 2;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log("[BaseContentSet] Migration: stone_pickaxe tool tier 1 → 2 (new ore ladder).");
            }

            // The generic log item becomes placeable (places an oak log block)
            // — only if no placement link was ever authored.
            ItemDefinition log = FindItem("log");
            BlockDefinition oakLog = FindBlock("oak_log");
            if (log != null && oakLog != null && log.PlacedBlock == null)
            {
                var serialized = new SerializedObject(log);
                serialized.FindProperty("placedBlock").objectReferenceValue = oakLog;
                if ((ItemCategory)serialized.FindProperty("category").intValue == ItemCategory.Resource)
                    serialized.FindProperty("category").intValue = (int)ItemCategory.Block;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log("[BaseContentSet] Migration: 'log' item now places oak_log blocks.");
            }

            // Tree templates upgrade from the generic wood/leaves blocks to the
            // named variants — only while they still reference the generics.
            MigrateTreeTemplate("oak", "oak_log", "oak_leaves");
            MigrateTreeTemplate("pine", "pine_log", "pine_leaves");
        }

        private static void MigrateTreeTemplate(string templateId, string trunkBlockId, string leavesBlockId)
        {
            TreeTemplateDefinition template = AssetDatabase.FindAssets("t:TreeTemplateDefinition")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<TreeTemplateDefinition>)
                .FirstOrDefault(t => t != null && t.Id == templateId);
            if (template == null)
                return;

            bool genericTrunk = template.TrunkBlock != null && template.TrunkBlock.Id == "wood";
            bool genericLeaves = template.LeavesBlock != null && template.LeavesBlock.Id == "leaves";
            if (!genericTrunk && !genericLeaves)
                return;

            var serialized = new SerializedObject(template);
            if (genericTrunk)
                serialized.FindProperty("trunkBlock").objectReferenceValue = FindBlock(trunkBlockId);
            if (genericLeaves)
                serialized.FindProperty("leavesBlock").objectReferenceValue = FindBlock(leavesBlockId);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[BaseContentSet] Migration: tree template '{templateId}' now uses {trunkBlockId}/{leavesBlockId}.");
        }

        // ------------------------------------------------------------------
        // Shared helpers (the sub-generators call these)
        // ------------------------------------------------------------------

        /// <summary>Deterministic per-ID seed so reruns regenerate byte-identical textures.</summary>
        internal static int StableSeed(string id)
        {
            unchecked
            {
                int hash = 23;
                foreach (char c in id)
                    hash = hash * 31 + c;
                return hash == 0 ? 1 : hash;
            }
        }

        /// <summary>Generates + writes a block texture (optionally post-processed, e.g. ore nuggets). Always regenerates.</summary>
        internal static Texture2D BlockTexture(
            string id, TextureStyle style, Color color, float hueShift = 0f,
            System.Action<Color32[], int, int> postProcess = null)
        {
            int seed = StableSeed(id);
            Color32[] pixels = TextureSynth.GeneratePixels(style, color, TextureSynth.DefaultBlockSize, seed, hueShift);
            postProcess?.Invoke(pixels, TextureSynth.DefaultBlockSize, seed);

            TexturesWritten++;
            return GeneratedTextureAssets.WriteBlockTexture(
                $"{GeneratedTextureAssets.BlockTextureFolder}/gen_{id}.png", pixels, TextureSynth.DefaultBlockSize);
        }

        /// <summary>Generates + writes an item icon sprite. Always regenerates.</summary>
        internal static Sprite Icon(string id, IconShape shape, Color primary, Color secondary)
        {
            Color32[] pixels = TextureSynth.GenerateIconPixels(
                shape, primary, secondary, TextureSynth.DefaultIconSize, StableSeed(id));

            TexturesWritten++;
            return GeneratedTextureAssets.WriteIconSprite(
                $"{GeneratedTextureAssets.IconFolder}/gen_icon_{id}.png", pixels, TextureSynth.DefaultIconSize);
        }

        /// <summary>Ore look: stone base + a handful of small colored nugget clusters.</summary>
        internal static System.Action<Color32[], int, int> NuggetOverlay(Color nuggetColor)
        {
            return (pixels, size, seed) =>
            {
                var random = new System.Random(seed ^ 0x5EED);
                int clusters = 5 + random.Next(3);
                for (int i = 0; i < clusters; i++)
                {
                    int x = random.Next(size);
                    int y = random.Next(size);
                    int nuggets = 3 + random.Next(3);
                    for (int n = 0; n < nuggets; n++)
                    {
                        int px = Mathf.Clamp(x + random.Next(-1, 2), 0, size - 1);
                        int py = Mathf.Clamp(y + random.Next(-1, 2), 0, size - 1);
                        float shine = 0.9f + (float)random.NextDouble() * 0.3f;
                        pixels[py * size + px] = new Color(
                            Mathf.Clamp01(nuggetColor.r * shine),
                            Mathf.Clamp01(nuggetColor.g * shine),
                            Mathf.Clamp01(nuggetColor.b * shine), 1f);
                    }
                }
            };
        }

        internal static Color Preset(TextureStyle style, int index)
        {
            var presets = TexturePalettes.GetPresets(style);
            return presets[Mathf.Clamp(index, 0, presets.Count - 1)].Color;
        }

        // ---- Asset lookups (by stable string ID, wherever the asset lives) ----

        internal static ItemDefinition FindItem(string id)
        {
            return AssetDatabase.FindAssets("t:ItemDefinition")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ItemDefinition>)
                .FirstOrDefault(item => item != null && item.Id == id);
        }

        internal static BlockDefinition FindBlock(string id)
        {
            return AssetDatabase.FindAssets("t:BlockDefinition")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<BlockDefinition>)
                .FirstOrDefault(block => block != null && block.Id == id);
        }

        internal static BuildingPieceDefinition FindPiece(string id)
        {
            return AssetDatabase.FindAssets("t:BuildingPieceDefinition")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<BuildingPieceDefinition>)
                .FirstOrDefault(piece => piece != null && piece.Id == id);
        }
    }
}
