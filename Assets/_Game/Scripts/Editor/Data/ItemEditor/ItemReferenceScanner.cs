using System.Collections.Generic;
using IslandGame.Data.Blocks;
using IslandGame.Data.Crafting;
using IslandGame.Data.Items;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Inbound-reference check run before deleting an ItemDefinition, so the
    /// user is warned about references that would go null — the item-side
    /// counterpart of BlockReferenceScanner.
    ///
    /// Checks: block Drop Items (Phase 3) and recipe outputs/ingredients
    /// (Phase 8). Later phases APPEND further checks here (loot tables, ...) —
    /// extend FindReferences, never replace the pattern.
    /// </summary>
    internal static class ItemReferenceScanner
    {
        /// <summary>One human-readable line per asset referencing the item; empty list = safe to delete.</summary>
        public static List<string> FindReferences(ItemDefinition item)
        {
            var references = new List<string>();
            if (item == null)
                return references;

            var blockDatabase = Resources.Load<BlockDatabase>(BlockDatabase.ResourcesPath);
            if (blockDatabase == null)
            {
                references.Add("(BlockDatabase not found — block Drop Item references could NOT be checked. Run Island Game/Data/Sync Databases.)");
                return references;
            }

            foreach (BlockDefinition block in blockDatabase.All)
            {
                if (block != null && block.DropItem == item)
                    references.Add($"Block '{block.DisplayName}' ({block.Id}) — Drop Item");
            }

            var recipeDatabase = Resources.Load<RecipeDatabase>(RecipeDatabase.ResourcesPath);
            if (recipeDatabase == null)
                return references; // pre-Phase-8 project state; nothing to check

            foreach (RecipeDefinition recipe in recipeDatabase.All)
            {
                if (recipe == null)
                    continue;

                if (recipe.Output == item)
                    references.Add($"Recipe '{recipe.DisplayName}' ({recipe.Id}) — Output");

                foreach (RecipeIngredient ingredient in recipe.Ingredients)
                {
                    if (ingredient != null && ingredient.Item == item)
                        references.Add($"Recipe '{recipe.DisplayName}' ({recipe.Id}) — Ingredient");
                }
            }

            return references;
        }
    }
}
