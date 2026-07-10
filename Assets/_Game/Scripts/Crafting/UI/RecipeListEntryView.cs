using IslandGame.Data.Crafting;
using UnityEngine;
using UnityEngine.UI;

namespace IslandGame.Crafting.UI
{
    /// <summary>
    /// One row in the crafting menu's recipe list: output icon + name, click
    /// to select. The label greys out when the recipe isn't currently
    /// craftable and the row tints when selected. Pooled and rebound by
    /// CraftingMenuController on every filter/inventory change.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RecipeListEntryView : MonoBehaviour
    {
        private static readonly Color SelectedTint = new Color(0.24f, 0.49f, 0.91f, 0.4f);
        private static readonly Color NormalTint = new Color(0.12f, 0.12f, 0.14f, 0.9f);

        [Header("Wired by the UI builder (on the template)")]
        [SerializeField] private Image background;
        [SerializeField] private Image icon;
        [SerializeField] private Text label;

        private CraftingMenuController controller;
        private RecipeDefinition recipe;

        private void Awake()
        {
            GetComponent<Button>().onClick.AddListener(() =>
            {
                if (controller != null && recipe != null)
                    controller.SelectRecipe(recipe);
            });
        }

        public void Bind(CraftingMenuController owner, RecipeDefinition boundRecipe, bool craftable, bool selected)
        {
            controller = owner;
            recipe = boundRecipe;

            string displayName = string.IsNullOrEmpty(recipe.DisplayName) ? recipe.name : recipe.DisplayName;
            label.text = !recipe.IsBuildingRecipe && recipe.OutputCount > 1
                ? $"{displayName} ×{recipe.OutputCount}"
                : displayName;
            label.color = craftable ? Color.white : new Color(1f, 1f, 1f, 0.45f);

            Sprite outputIcon = recipe.Output != null ? recipe.Output.Icon
                : recipe.OutputPiece != null ? recipe.OutputPiece.Icon : null;
            icon.enabled = outputIcon != null;
            icon.sprite = outputIcon;

            background.color = selected ? SelectedTint : NormalTint;
        }
    }
}
