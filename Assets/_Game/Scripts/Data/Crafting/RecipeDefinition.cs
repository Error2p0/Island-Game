using System;
using System.Collections.Generic;
using IslandGame.Data.Building;
using IslandGame.Data.Items;
using UnityEngine;

namespace IslandGame.Data.Crafting
{
    /// <summary>One ingredient line of a recipe: an item and how many of it.</summary>
    [Serializable]
    public sealed class RecipeIngredient
    {
        [SerializeField] private ItemDefinition item;

        [Min(1)]
        [SerializeField] private int count = 1;

        public ItemDefinition Item => item;
        public int Count => count;
    }

    /// <summary>
    /// Authored data for one crafting recipe. Pure data — CraftingSystem
    /// executes it, the crafting menu displays it, the Recipe Editor authors
    /// it. Registered in the RecipeDatabase automatically on import, same as
    /// items and blocks. Ingredients and output reference ItemDefinitions
    /// directly (authored cross-links, Phase 1 convention); only the string ID
    /// ever goes into save data.
    /// </summary>
    [CreateAssetMenu(fileName = "NewRecipe", menuName = "Island Game/Recipe Definition")]
    public sealed class RecipeDefinition : ScriptableObject, IDefinition
    {
        [Header("Identity")]
        [Tooltip("Stable unique ID (lowercase_underscore). Serialized into future save data (unlock states etc.) — never change it once used. Auto-filled from the asset name when empty.")]
        [SerializeField] private string id;

        [Tooltip("Name shown in the crafting menu. Usually the output's name, but explicit so variants ('Planks (efficient)') can differ.")]
        [SerializeField] private string displayName;

        [Header("Output — set EXACTLY ONE of Output (item) / Output Piece (building)")]
        [Tooltip("Item landed in the inventory when crafted. Leave empty for building recipes.")]
        [SerializeField] private ItemDefinition output;

        [Min(1)]
        [SerializeField] private int outputCount = 1;

        [Tooltip("Building piece this recipe lets the player PLACE (arms the placement ghost from the crafting menu; ingredients are consumed on successful placement, not on 'craft'). Leave empty for item recipes. Output Count is ignored — placement is one piece per confirm.")]
        [SerializeField] private BuildingPieceDefinition outputPiece;

        [Header("Ingredients")]
        [SerializeField] private List<RecipeIngredient> ingredients = new List<RecipeIngredient>();

        [Header("Requirements")]
        [Tooltip("Station that must be near the player. None = hand-craftable anywhere.")]
        [SerializeField] private CraftingStationType station = CraftingStationType.None;

        [Tooltip("Seconds the craft takes. 0 = instant.")]
        [Min(0f)]
        [SerializeField] private float craftSeconds;

        public string Id => id;
        public string DisplayName => displayName;
        public ItemDefinition Output => output;
        public int OutputCount => outputCount;
        public BuildingPieceDefinition OutputPiece => outputPiece;
        public IReadOnlyList<RecipeIngredient> Ingredients => ingredients;
        public CraftingStationType Station => station;
        public float CraftSeconds => craftSeconds;

        /// <summary>
        /// True when this recipe produces a placeable building piece instead of
        /// an inventory item: the crafting menu's Build button arms the
        /// placement ghost, and BuildingPlacementController consumes the
        /// ingredients on successful placement. (The deliberate, flagged
        /// alternative — a "portable kit" — is a normal ITEM recipe whose
        /// output item carries a PlacedPiece link.)
        /// </summary>
        public bool IsBuildingRecipe => outputPiece != null;

        /// <summary>
        /// True when the output references are missing/contradictory (neither
        /// or both outputs set, deleted assets) or any ingredient is missing.
        /// The Recipe Editor flags it; crafting and placement refuse it.
        /// </summary>
        public bool HasDanglingReferences
        {
            get
            {
                bool hasExactlyOneOutput = (output != null) ^ (outputPiece != null);
                if (!hasExactlyOneOutput)
                    return true;

                for (int i = 0; i < ingredients.Count; i++)
                {
                    if (ingredients[i] == null || ingredients[i].Item == null)
                        return true;
                }

                return false;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Convenience only: a fresh asset inherits its name as ID/display name.
            // An existing ID is never regenerated — stability beats tidiness.
            if (string.IsNullOrWhiteSpace(id) && !string.IsNullOrEmpty(name))
                id = name.Trim().ToLowerInvariant().Replace(' ', '_');
            else if (id != null)
                id = id.Trim();

            if (string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrEmpty(name))
                displayName = name;
        }
#endif
    }
}
