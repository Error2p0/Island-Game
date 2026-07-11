using IslandGame.Data.Crafting;
using UnityEditor;
using static IslandGame.EditorTools.Data.BaseContentSetGenerator;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Every recipe of the base content set, gated so the whole tree is
    /// reachable from a fresh spawn:
    ///
    ///   HAND       punch trees → logs → planks → wooden tools, spear, club,
    ///              torch, flint, cobblestone, basic pieces (half-wall,
    ///              pillar, fence, torch stand)
    ///   CAMPFIRE   cooking (roasted berries)
    ///   WORKBENCH  stone tools, cut stone/brick, bow + arrows, door/window/
    ///              stairs, furnace, chest, bed, boat, all copper gear
    ///   FURNACE    ore → bars (the metal gate the copper gear sits behind)
    /// </summary>
    internal static class ContentSetRecipes
    {
        public static void Create()
        {
            // ---- Hand ----------------------------------------------------
            Item("plank_oak_from_log", "Oak Planks", "plank_oak", 4, CraftingStationType.None, 0f, ("log", 1));
            Item("plank_birch_from_log", "Birch Planks", "plank_birch", 4, CraftingStationType.None, 0f, ("log", 1));
            Item("plank_pine_from_log", "Pine Planks", "plank_pine", 4, CraftingStationType.None, 0f, ("log", 1));
            Item("cobblestone_from_stone", "Cobblestone", "cobblestone", 2, CraftingStationType.None, 0f, ("stone", 2));
            Item("flint_knapping", "Flint", "flint", 1, CraftingStationType.None, 0f, ("stone", 2));
            Item("torch_craft", "Torch", "torch", 2, CraftingStationType.None, 0f, ("wood_plank", 1), ("plant_fiber", 1));

            Item("wooden_axe_craft", "Wooden Axe", "wooden_axe", 1, CraftingStationType.None, 1f, ("wood_plank", 3), ("plant_fiber", 1));
            Item("wooden_pickaxe_craft", "Wooden Pickaxe", "wooden_pickaxe", 1, CraftingStationType.None, 1f, ("wood_plank", 3), ("plant_fiber", 1));
            Item("wooden_shovel_craft", "Wooden Shovel", "wooden_shovel", 1, CraftingStationType.None, 1f, ("wood_plank", 2), ("plant_fiber", 1));
            Item("wooden_hoe_craft", "Wooden Hoe", "wooden_hoe", 1, CraftingStationType.None, 1f, ("wood_plank", 2), ("plant_fiber", 1));
            Item("wooden_spear_craft", "Wooden Spear", "wooden_spear", 1, CraftingStationType.None, 1f, ("wood_plank", 2), ("flint", 1));
            Item("wooden_club_craft", "Wooden Club", "wooden_club", 1, CraftingStationType.None, 1f, ("wood_plank", 2));

            // ---- Campfire (cooking) ---------------------------------------
            Item("cooked_berries_craft", "Roasted Berries", "cooked_berries", 1, CraftingStationType.Campfire, 2f, ("berries", 2));

            // ---- Workbench -------------------------------------------------
            Item("cut_stone_craft", "Cut Stone", "cut_stone", 1, CraftingStationType.Workbench, 1f, ("stone", 2));
            Item("stone_brick_craft", "Stone Brick", "stone_brick", 2, CraftingStationType.Workbench, 1f, ("cut_stone", 2));

            Item("stone_axe_craft", "Stone Axe", "stone_axe", 1, CraftingStationType.Workbench, 2f, ("stone", 3), ("wood_plank", 2));
            Item("stone_shovel_craft", "Stone Shovel", "stone_shovel", 1, CraftingStationType.Workbench, 2f, ("stone", 2), ("wood_plank", 2));
            Item("stone_hoe_craft", "Stone Hoe", "stone_hoe", 1, CraftingStationType.Workbench, 2f, ("stone", 2), ("wood_plank", 2));
            // (the Stone Pickaxe recipe has existed since the crafting phase)

            Item("arrow_craft", "Arrows", "arrow", 4, CraftingStationType.Workbench, 1f, ("wood_plank", 1), ("flint", 1));
            Item("bow_craft", "Bow", "bow", 1, CraftingStationType.Workbench, 2f, ("wood_plank", 3), ("plant_fiber", 2));
            Item("bucket_craft", "Bucket", "bucket", 1, CraftingStationType.Workbench, 2f, ("copper_bar", 2));

            Item("copper_axe_craft", "Copper Axe", "copper_axe", 1, CraftingStationType.Workbench, 3f, ("copper_bar", 3), ("wood_plank", 2));
            Item("copper_pickaxe_craft", "Copper Pickaxe", "copper_pickaxe", 1, CraftingStationType.Workbench, 3f, ("copper_bar", 3), ("wood_plank", 2));
            Item("copper_shovel_craft", "Copper Shovel", "copper_shovel", 1, CraftingStationType.Workbench, 3f, ("copper_bar", 2), ("wood_plank", 2));
            Item("copper_hoe_craft", "Copper Hoe", "copper_hoe", 1, CraftingStationType.Workbench, 3f, ("copper_bar", 2), ("wood_plank", 2));
            Item("copper_sword_craft", "Copper Sword", "copper_sword", 1, CraftingStationType.Workbench, 3f, ("copper_bar", 2), ("wood_plank", 1));

            // ---- Furnace (smelting) -----------------------------------------
            Item("copper_bar_smelt", "Copper Bar", "copper_bar", 1, CraftingStationType.Furnace, 3f, ("raw_copper", 2));
            Item("tin_bar_smelt", "Tin Bar", "tin_bar", 1, CraftingStationType.Furnace, 3f, ("raw_tin", 2));
            Item("silver_bar_smelt", "Silver Bar", "silver_bar", 1, CraftingStationType.Furnace, 3f, ("raw_silver", 2));

            // ---- Building pieces --------------------------------------------
            Building("build_half_wall_wood", "Wooden Half-Wall", "half_wall_wood", CraftingStationType.None, ("wood_plank", 3));
            Building("build_pillar_wood", "Support Pillar", "pillar_wood", CraftingStationType.None, ("wood_plank", 2));
            Building("build_fence_wood", "Wooden Fence", "fence_wood", CraftingStationType.None, ("wood_plank", 3));
            Building("build_torch_stand", "Torch Stand", "torch_stand", CraftingStationType.None, ("wood_plank", 2), ("torch", 1));

            Building("build_window_wood", "Wooden Window", "window_wood", CraftingStationType.Workbench, ("wood_plank", 5));
            Building("build_stairs_wood", "Wooden Stairs", "stairs_wood", CraftingStationType.Workbench, ("wood_plank", 6));
            Building("build_door_wood", "Wooden Door", "door_wood", CraftingStationType.Workbench, ("wood_plank", 4));
            Building("build_furnace", "Furnace", "furnace", CraftingStationType.Workbench, ("stone", 8), ("clay_lump", 2));
            Building("build_chest", "Chest", "chest", CraftingStationType.Workbench, ("wood_plank", 8));
            Building("build_bed", "Bed", "bed", CraftingStationType.Workbench, ("wood_plank", 6), ("plant_fiber", 4));
            Building("build_boat", "Boat", "boat", CraftingStationType.Workbench, ("wood_plank", 10), ("plant_fiber", 2));
        }

        // ==================================================================
        // Helpers (create-if-missing, same as every content creator)
        // ==================================================================

        private static void Item(
            string recipeId, string displayName, string outputItemId, int outputCount,
            CraftingStationType station, float craftSeconds, params (string id, int count)[] ingredients)
        {
            var output = FindItem(outputItemId);
            if (output == null)
            {
                UnityEngine.Debug.LogWarning($"[BaseContentSet] Recipe '{recipeId}' skipped — output item '{outputItemId}' missing.");
                return;
            }

            RecipeDefinition recipe = ExampleContentCreator.CreateOrLoad<RecipeDefinition>(
                $"{RecipeFolder}/{ContentSetItemsAndBlocks.ToAssetName(recipeId)}.asset", out bool created);
            if (!created)
                return;

            var serialized = new SerializedObject(recipe);
            serialized.FindProperty("id").stringValue = recipeId;
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("output").objectReferenceValue = output;
            serialized.FindProperty("outputPiece").objectReferenceValue = null;
            serialized.FindProperty("outputCount").intValue = outputCount;
            WriteRequirements(serialized, station, craftSeconds, ingredients);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            RecipesCreated++;
        }

        private static void Building(
            string recipeId, string displayName, string pieceId,
            CraftingStationType station, params (string id, int count)[] ingredients)
        {
            var piece = FindPiece(pieceId);
            if (piece == null)
            {
                UnityEngine.Debug.LogWarning($"[BaseContentSet] Recipe '{recipeId}' skipped — piece '{pieceId}' missing.");
                return;
            }

            RecipeDefinition recipe = ExampleContentCreator.CreateOrLoad<RecipeDefinition>(
                $"{RecipeFolder}/{ContentSetItemsAndBlocks.ToAssetName(recipeId)}.asset", out bool created);
            if (!created)
                return;

            var serialized = new SerializedObject(recipe);
            serialized.FindProperty("id").stringValue = recipeId;
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("output").objectReferenceValue = null;
            serialized.FindProperty("outputPiece").objectReferenceValue = piece;
            serialized.FindProperty("outputCount").intValue = 1;
            WriteRequirements(serialized, station, 0f, ingredients);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            RecipesCreated++;
        }

        private static void WriteRequirements(
            SerializedObject serialized, CraftingStationType station, float craftSeconds,
            (string id, int count)[] ingredients)
        {
            serialized.FindProperty("station").intValue = (int)station;
            serialized.FindProperty("craftSeconds").floatValue = craftSeconds;

            SerializedProperty list = serialized.FindProperty("ingredients");
            list.arraySize = 0;
            foreach ((string id, int count) in ingredients)
            {
                var item = FindItem(id);
                if (item == null)
                {
                    UnityEngine.Debug.LogWarning($"[BaseContentSet] Ingredient '{id}' missing — dropped from a recipe.");
                    continue;
                }

                list.arraySize++;
                SerializedProperty element = list.GetArrayElementAtIndex(list.arraySize - 1);
                element.FindPropertyRelative("item").objectReferenceValue = item;
                element.FindPropertyRelative("count").intValue = count;
            }
        }
    }
}
