using System.Collections.Generic;
using IslandGame.Data.Building;
using UnityEngine;

namespace IslandGame.Data.Crafting
{
    /// <summary>
    /// Registry of every RecipeDefinition in the project. Same pattern as
    /// ItemDatabase/BlockDatabase: lives at
    /// Assets/_Game/Resources/Databases/RecipeDatabase.asset, created and kept
    /// in sync by Island Game/Data/Sync Databases (automatic on recipe
    /// import/delete/move) — no CreateAssetMenu, never create one by hand.
    /// </summary>
    public sealed class RecipeDatabase : DefinitionDatabase<RecipeDefinition>
    {
        /// <summary>Path under Resources/ — must match DefinitionDatabaseSync.DatabaseFolder.</summary>
        public const string ResourcesPath = "Databases/RecipeDatabase";

        private static RecipeDatabase instance;

        /// <summary>Global access for runtime systems (crafting menu, CraftingSystem).</summary>
        public static RecipeDatabase Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = Resources.Load<RecipeDatabase>(ResourcesPath);
                    if (instance == null)
                        Debug.LogError(
                            $"RecipeDatabase asset not found at Resources/{ResourcesPath}. " +
                            "Run Island Game/Data/Sync Databases in the editor to create it.");
                }

                return instance;
            }
        }

        /// <summary>
        /// The building recipe that places the given piece, or null when the
        /// piece has none (it then places for free — creative-style content).
        /// Linear scan by design: called on piece selection and placement
        /// confirm, not per frame, and recipe counts stay small. First match
        /// wins; the Recipe Editor warns about duplicates.
        /// </summary>
        public RecipeDefinition FindForPiece(BuildingPieceDefinition piece)
        {
            if (piece == null)
                return null;

            IReadOnlyList<RecipeDefinition> all = All;
            for (int i = 0; i < all.Count; i++)
            {
                RecipeDefinition recipe = all[i];
                if (recipe != null && recipe.OutputPiece == piece)
                    return recipe;
            }

            return null;
        }
    }
}
