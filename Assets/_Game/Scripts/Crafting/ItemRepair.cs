using System.Text;
using IslandGame.Data.Crafting;
using IslandGame.Data.Items;
using IslandGame.Inventory;
using UnityEngine;

namespace IslandGame.Crafting
{
    /// <summary>
    /// Workbench repair logic, kept out of the behavior so future stations
    /// (anvil?) reuse it. THE COST MODEL: a repair consumes the item's own
    /// CRAFTING recipe ingredients at a fraction, scaled by how damaged the
    /// item is (never below 1 of each ingredient), and restores durability to
    /// max. Reusing the recipe was chosen over a separate repair-recipe data
    /// type because it derives costs for every current and future tool with
    /// zero extra authoring, can't drift out of sync with the craft cost, and
    /// no existing content wants different repair materials — the day one
    /// does, a nullable "repair overrides" list on RecipeDefinition beats a
    /// parallel asset type. Damage scaling (missing01) makes topping off a
    /// barely-worn tool cheap instead of always charging the flat fraction.
    ///
    /// Items without a (non-building) recipe for their exact definition are
    /// not repairable — notably Broken Variants, which is intentional: a
    /// broken tool is re-crafted, not repaired, or it would undercut crafting.
    /// </summary>
    public static class ItemRepair
    {
        /// <summary>Reused string builder — repair prompts/messages are built at interaction rate, not per frame.</summary>
        private static readonly StringBuilder MessageBuilder = new StringBuilder(128);

        /// <summary>The recipe whose ingredients price this item's repair: the first item recipe producing it.</summary>
        public static RecipeDefinition FindCraftRecipe(ItemDefinition item)
        {
            if (item == null || RecipeDatabase.Instance == null)
                return null;

            var recipes = RecipeDatabase.Instance.All;
            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeDefinition recipe = recipes[i];
                if (recipe != null && !recipe.IsBuildingRecipe && !recipe.HasDanglingReferences
                    && recipe.Output == item)
                    return recipe;
            }

            return null;
        }

        /// <summary>Ingredient count charged for one repair line at the given damage level.</summary>
        public static int GetRepairCost(int recipeCount, float costFraction, float missing01)
        {
            return Mathf.Max(1, Mathf.CeilToInt(recipeCount * costFraction * missing01));
        }

        /// <summary>
        /// Attempts to fully repair the given slot. Returns true when durability
        /// was restored (ingredients consumed); message always explains the
        /// outcome for logging/prompt use — including WHY a repair failed
        /// (no recipe, missing materials).
        /// </summary>
        public static bool TryRepairSlot(
            InventorySystem inventory, int slotIndex, float costFraction, out string message)
        {
            if (inventory == null)
            {
                message = "No inventory.";
                return false;
            }

            InventorySlot slot = inventory.GetSlot(slotIndex);
            if (slot.IsEmpty || !slot.Item.HasDurability)
            {
                message = "Nothing repairable equipped.";
                return false;
            }

            ItemDefinition item = slot.Item;
            float missing01 = 1f - slot.Durability01;
            if (missing01 <= 0f)
            {
                message = $"{item.DisplayName} is already at full durability.";
                return false;
            }

            RecipeDefinition recipe = FindCraftRecipe(item);
            if (recipe == null)
            {
                message = $"{item.DisplayName} cannot be repaired — it has no crafting recipe to derive a cost from.";
                return false;
            }

            // Validate the full bill before consuming anything (crafting's
            // consume-nothing-on-any-failure rule applies to repairs too).
            var ingredients = recipe.Ingredients;
            for (int i = 0; i < ingredients.Count; i++)
            {
                int need = GetRepairCost(ingredients[i].Count, costFraction, missing01);
                int have = inventory.GetItemCount(ingredients[i].Item);
                if (have < need)
                {
                    message = $"Repair needs {need} × {ingredients[i].Item.DisplayName} (you have {have}).";
                    return false;
                }
            }

            MessageBuilder.Clear();
            MessageBuilder.Append("Repaired ").Append(item.DisplayName).Append(" for");
            for (int i = 0; i < ingredients.Count; i++)
            {
                int need = GetRepairCost(ingredients[i].Count, costFraction, missing01);
                inventory.RemoveItem(ingredients[i].Item, need);
                MessageBuilder.Append(i == 0 ? " " : ", ")
                    .Append(need).Append(" × ").Append(ingredients[i].Item.DisplayName);
            }

            MessageBuilder.Append('.');

            inventory.SetSlotDurability01(slotIndex, 1f);
            message = MessageBuilder.ToString();
            return true;
        }
    }
}
