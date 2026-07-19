using IslandGame.Data.Building;
using IslandGame.Data.Crafting;
using IslandGame.Data.Items;
using IslandGame.Inventory;
using UnityEngine;

namespace IslandGame.Building
{
    /// <summary>
    /// The single implementation of "give back part of a piece's build cost",
    /// shared by deconstruction (refunds into the remover's inventory,
    /// overflow drops as WorldItems) and damage-destruction (no inventory —
    /// everything drops at the piece). Cost authority is the linked building
    /// recipe (Phase 3 — the same recipe placement charges), with the
    /// definition's legacy MaterialCost lines as the recipe-less fallback.
    /// Each line is Ratio × cost, floored, but never below 1 while the line
    /// cost anything.
    /// </summary>
    public static class BuildingRefund
    {
        /// <summary>Refund fraction for pieces destroyed by damage. Deconstruction passes its own serialized ratio instead.</summary>
        public const float DestroyedRatio = 0.5f;

        /// <summary>
        /// Refunds part of the piece's build cost. A null inventory (piece
        /// smashed by damage, attacker may not even be the player) drops
        /// every refund line at the piece.
        /// </summary>
        public static void Refund(BuildingPiece piece, InventorySystem inventory, float ratio)
        {
            BuildingPieceDefinition definition = piece.Definition;
            if (definition == null)
                return; // unresolved scene-authored piece: nothing to refund

            Vector3 dropPoint = piece.transform.position + Vector3.up * 0.5f;

            RecipeDatabase recipes = RecipeDatabase.Instance;
            RecipeDefinition recipe = recipes != null ? recipes.FindForPiece(definition) : null;

            if (recipe != null)
            {
                foreach (RecipeIngredient ingredient in recipe.Ingredients)
                {
                    if (ingredient != null && ingredient.Item != null)
                        RefundLine(ingredient.Item, ingredient.Count, ratio, inventory, dropPoint);
                }
            }
            else
            {
                for (int i = 0; i < definition.MaterialCost.Count; i++)
                {
                    BuildingMaterialCost line = definition.MaterialCost[i];
                    if (line != null && line.Item != null)
                        RefundLine(line.Item, line.Count, ratio, inventory, dropPoint);
                }
            }
        }

        private static void RefundLine(ItemDefinition item, int cost, float ratio, InventorySystem inventory, Vector3 dropPoint)
        {
            if (cost <= 0)
                return;

            int refund = Mathf.Max(1, Mathf.FloorToInt(cost * ratio));

            int added = inventory != null ? inventory.AddItem(item, refund) : 0;
            int leftover = refund - added;
            if (leftover > 0)
            {
                // No inventory, or it's full: the refund falls at the piece
                // instead of vanishing — the same overflow rule mining uses.
                WorldItem.Spawn(item, leftover, 1f, dropPoint, Vector3.up * 1.5f);
            }
        }
    }
}
