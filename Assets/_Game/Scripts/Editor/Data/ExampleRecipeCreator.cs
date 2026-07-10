using System.Linq;
using IslandGame.Data.Blocks;
using IslandGame.Data.Crafting;
using IslandGame.Data.Items;
using UnityEditor;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Creates the Phase 8 example recipes proving the crafting loop end to
    /// end — Wood Planks ×4 from a Log (hand, instant), Stone ×1 from Sand ×4
    /// (hand, timed), Stone Pickaxe from Stone + Planks (Workbench, timed) —
    /// plus the Stone Pickaxe tool item itself (tier 1, efficient against
    /// stone) since no tool item existed yet. Same idempotent pipeline as the
    /// other content creators; everything is editable in the Recipe/Item
    /// Editor windows afterwards.
    /// </summary>
    public static class ExampleRecipeCreator
    {
        private const string RecipeFolder = "Assets/_Game/Content/Recipes";
        private const string ItemFolder = "Assets/_Game/Content/Items";
        private const string IconFolder = "Assets/_Game/Content/Textures/Icons";

        [MenuItem("Island Game/Data/Create Example Recipes")]
        public static void Create()
        {
            ItemDefinition log = FindItem("log");
            ItemDefinition stone = FindItem("stone");
            ItemDefinition woodPlank = FindItem("wood_plank");
            ItemDefinition sand = FindItem("sand");
            BlockDefinition stoneBlock = FindBlock("stone");

            if (log == null || stone == null || woodPlank == null)
            {
                Debug.LogError("Example recipes need the Phase 1 example items (log/stone/wood_plank) — run Island Game/Data/Create Example Content first.");
                return;
            }

            DefinitionDatabaseSync.EnsureFolderExists(RecipeFolder);
            DefinitionDatabaseSync.EnsureFolderExists(ItemFolder);
            DefinitionDatabaseSync.EnsureFolderExists(IconFolder);

            // --- Stone Pickaxe tool item ---------------------------------
            Sprite pickaxeIcon = ExampleContentCreator.LoadSprite(
                ExampleContentCreator.CreateTexture($"{IconFolder}/icon_stone_pickaxe.png", 64, PickaxeIconPixel, asSprite: true));

            ItemDefinition pickaxe = ExampleContentCreator.CreateOrLoad<ItemDefinition>(
                $"{ItemFolder}/StonePickaxe.asset", out bool pickaxeCreated);
            if (pickaxeCreated)
            {
                ExampleContentCreator.SetItemFields(pickaxe,
                    id: "stone_pickaxe", displayName: "Stone Pickaxe",
                    description: "A crude but honest mining tool. Breaks stone; embarrasses sand.",
                    icon: pickaxeIcon, category: ItemCategory.Tool, maxStackSize: 1, weightKg: 3f);
                SetToolFields(pickaxe, ToolType.Pickaxe, toolTier: 1, miningSpeedMultiplier: 4f, stoneBlock);
            }

            // Fill-if-zero upgrade (small-voxel mining phase): pre-existing
            // pickaxe assets get the carve radius; an authored nonzero value
            // is never touched.
            if (pickaxe.MiningRadius <= 0f)
            {
                var pickaxeSerialized = new SerializedObject(pickaxe);
                pickaxeSerialized.FindProperty("miningRadius").floatValue = 0.55f;
                pickaxeSerialized.ApplyModifiedPropertiesWithoutUndo();
            }

            // --- Recipes ---------------------------------------------------
            RecipeDefinition planks = ExampleContentCreator.CreateOrLoad<RecipeDefinition>(
                $"{RecipeFolder}/PlanksFromLog.asset", out bool planksCreated);
            if (planksCreated)
            {
                SetRecipeFields(planks, "planks_from_log", "Wood Planks",
                    output: woodPlank, outputCount: 4,
                    ingredients: new[] { (log, 1) },
                    station: CraftingStationType.None, craftSeconds: 0f);
            }

            RecipeDefinition stoneFromSand = null;
            if (sand != null)
            {
                stoneFromSand = ExampleContentCreator.CreateOrLoad<RecipeDefinition>(
                    $"{RecipeFolder}/StoneFromSand.asset", out bool stoneFromSandCreated);
                if (stoneFromSandCreated)
                {
                    SetRecipeFields(stoneFromSand, "stone_from_sand", "Compacted Stone",
                        output: stone, outputCount: 1,
                        ingredients: new[] { (sand, 4) },
                        station: CraftingStationType.None, craftSeconds: 2f);
                }
            }
            else
            {
                Debug.LogWarning("Item 'sand' not found (run Island Game/Data/Create World Gen Content) — skipping the Compacted Stone recipe.");
            }

            RecipeDefinition pickaxeRecipe = ExampleContentCreator.CreateOrLoad<RecipeDefinition>(
                $"{RecipeFolder}/StonePickaxeRecipe.asset", out bool pickaxeRecipeCreated);
            if (pickaxeRecipeCreated)
            {
                SetRecipeFields(pickaxeRecipe, "stone_pickaxe", "Stone Pickaxe",
                    output: pickaxe, outputCount: 1,
                    ingredients: new[] { (stone, 3), (woodPlank, 2) },
                    station: CraftingStationType.Workbench, craftSeconds: 2f);
            }

            AssetDatabase.SaveAssets();
            DefinitionDatabaseSync.SyncAll();

            Debug.Log(
                "Example recipes ready: Wood Planks (hand/instant), Compacted Stone (hand/2s), Stone Pickaxe " +
                "(Workbench/2s) + the Stone Pickaxe tool item. Workbench testing: empty GameObject + BoxCollider + " +
                "CraftingStationMarker (type Workbench) near the player.");
        }

        // ------------------------------------------------------------------
        // Field setters
        // ------------------------------------------------------------------

        private static void SetToolFields(
            ItemDefinition item, ToolType toolType, int toolTier, float miningSpeedMultiplier, BlockDefinition efficientBlock)
        {
            var serialized = new SerializedObject(item);
            serialized.FindProperty("isTool").boolValue = true;
            serialized.FindProperty("toolType").intValue = (int)toolType;
            serialized.FindProperty("toolTier").intValue = toolTier;
            serialized.FindProperty("miningSpeedMultiplier").floatValue = miningSpeedMultiplier;
            serialized.FindProperty("holdType").intValue = (int)HoldType.OneHanded;
            serialized.FindProperty("holdSocket").intValue = (int)HoldSocket.RightHand;

            SerializedProperty efficient = serialized.FindProperty("efficientBlocks");
            efficient.arraySize = efficientBlock != null ? 1 : 0;
            if (efficientBlock != null)
                efficient.GetArrayElementAtIndex(0).objectReferenceValue = efficientBlock;

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetRecipeFields(
            RecipeDefinition recipe, string id, string displayName,
            ItemDefinition output, int outputCount,
            (ItemDefinition item, int count)[] ingredients,
            CraftingStationType station, float craftSeconds)
        {
            var serialized = new SerializedObject(recipe);
            serialized.FindProperty("id").stringValue = id;
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("output").objectReferenceValue = output;
            serialized.FindProperty("outputCount").intValue = outputCount;
            serialized.FindProperty("station").intValue = (int)station;
            serialized.FindProperty("craftSeconds").floatValue = craftSeconds;

            SerializedProperty list = serialized.FindProperty("ingredients");
            list.arraySize = ingredients.Length;
            for (int i = 0; i < ingredients.Length; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("item").objectReferenceValue = ingredients[i].item;
                element.FindPropertyRelative("count").intValue = ingredients[i].count;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------
        // Lookups (by string ID, wherever the asset lives)
        // ------------------------------------------------------------------

        private static ItemDefinition FindItem(string id)
        {
            return AssetDatabase.FindAssets("t:ItemDefinition")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ItemDefinition>)
                .FirstOrDefault(item => item != null && item.Id == id);
        }

        private static BlockDefinition FindBlock(string id)
        {
            return AssetDatabase.FindAssets("t:BlockDefinition")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<BlockDefinition>)
                .FirstOrDefault(block => block != null && block.Id == id);
        }

        // ------------------------------------------------------------------
        // Icon
        // ------------------------------------------------------------------

        private static Color32 PickaxeIconPixel(int x, int y, int size, System.Random random)
        {
            float u = (x + 0.5f) / size;
            float v = (y + 0.5f) / size;

            // Head: an arc hugging the top-right corner.
            float du = 1f - u;
            float dv = 1f - v;
            float cornerDistance = Mathf.Sqrt(du * du + dv * dv);
            if (cornerDistance > 0.18f && cornerDistance < 0.42f)
            {
                float shade = 0.55f + (float)(random.NextDouble() - 0.5) * 0.1f;
                return new Color32(
                    (byte)(shade * 255f), (byte)(shade * 255f), (byte)(Mathf.Min(1f, shade + 0.03f) * 255f), 255);
            }

            // Handle: diagonal shaft from bottom-left toward the head.
            if (Mathf.Abs(u - v) < 0.07f && cornerDistance >= 0.34f)
            {
                float shade = 0.5f + (float)(random.NextDouble() - 0.5) * 0.08f;
                return new Color32(
                    (byte)(shade * 0.9f * 255f), (byte)(shade * 0.62f * 255f), (byte)(shade * 0.38f * 255f), 255);
            }

            return new Color32(0, 0, 0, 0);
        }
    }
}
