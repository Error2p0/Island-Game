using System.Collections.Generic;
using IslandGame.Building;
using IslandGame.Creative.UI;
using IslandGame.Data.Crafting;
using IslandGame.Inventory;
using IslandGame.Inventory.UI;
using IslandGame.Player;
using UnityEngine;
using UnityEngine.UI;

namespace IslandGame.Crafting.UI
{
    /// <summary>
    /// The in-game crafting menu (B): recipe list from the RecipeDatabase with
    /// search, a craftable-only filter and All/Items/Building tabs, a detail
    /// panel with per-ingredient green/red have/need counters, station
    /// requirement with a clear reason when unmet, craft-time display, and a
    /// Craft button driving CraftingSystem (with a progress fill for timed
    /// crafts).
    ///
    /// BUILDING RECIPES (Phase 3): recipes with an Output Piece live under the
    /// Building tab, show the piece and its ingredient costs, and their button
    /// reads "Build" — clicking arms BuildingPlacementController with the
    /// piece and closes the menu so the player can aim and place. Ingredients
    /// are consumed by placement on confirm, never here.
    ///
    /// Same UI conventions as Phases 4/5: uGUI views built by an editor
    /// builder, UIInputFocus refcount for cursor/input, mutual exclusion with
    /// the inventory and creative screens (opens close them; their toggle
    /// keys close this).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CraftingMenuController : MonoBehaviour
    {
        private const float StationRefreshInterval = 0.25f;

        [Header("Scene References (wired by the UI builder)")]
        [SerializeField] private PlayerReferences playerReferences;
        [SerializeField] private InventorySystem inventory;
        [SerializeField] private CraftingSystem craftingSystem;
        [SerializeField] private InventoryUIController inventoryUI;
        [SerializeField] private CreativeMenuController creativeMenu;

        [Tooltip("Armed by building recipes' Build button. Auto-resolved when empty (canvases built before Phase 3).")]
        [SerializeField] private BuildingPlacementController placementController;

        [Header("View References (wired by the UI builder)")]
        [SerializeField] private GameObject panel;
        [SerializeField] private InputField searchField;
        [SerializeField] private Toggle craftableOnlyToggle;

        [Tooltip("All/Items/Building tab buttons. Optional — canvases built before Phase 3 simply show every recipe.")]
        [SerializeField] private Button tabAllButton;
        [SerializeField] private Button tabItemsButton;
        [SerializeField] private Button tabBuildingButton;
        [SerializeField] private GameObject recipeEntryTemplate;
        [SerializeField] private ScrollRect recipeScroll;
        [SerializeField] private Image outputIcon;
        [SerializeField] private Text outputName;
        [SerializeField] private Text stationLine;
        [SerializeField] private Text timeLine;
        [SerializeField] private GameObject ingredientRowTemplate;
        [SerializeField] private Button craftButton;
        [SerializeField] private Text craftButtonLabel;
        [SerializeField] private Image progressFill;
        [SerializeField] private Text reasonText;

        private readonly List<RecipeListEntryView> entryPool = new List<RecipeListEntryView>();
        private readonly List<IngredientRowView> rowPool = new List<IngredientRowView>();

        private enum RecipeTab
        {
            All,
            Items,
            Building,
        }

        private static readonly Color ActiveTabColor = new Color(0.24f, 0.49f, 0.91f, 0.9f);
        private static readonly Color InactiveTabColor = new Color(0.12f, 0.12f, 0.14f, 0.9f);

        private RecipeDatabase recipeDatabase;
        private RecipeDefinition selectedRecipe;
        private string search = string.Empty;
        private bool craftableOnly;
        private RecipeTab tab = RecipeTab.All;
        private bool initialized;
        private float nextStationRefresh;

        public bool IsOpen { get; private set; }

        private void Start()
        {
            panel.SetActive(false);
        }

        private void OnEnable()
        {
            playerReferences.InputHandler.CraftingTogglePressed += Toggle;
            playerReferences.InputHandler.InventoryTogglePressed += CloseFromOtherScreen;
            playerReferences.InputHandler.CreativeMenuTogglePressed += CloseFromOtherScreen;
        }

        private void OnDisable()
        {
            playerReferences.InputHandler.CraftingTogglePressed -= Toggle;
            playerReferences.InputHandler.InventoryTogglePressed -= CloseFromOtherScreen;
            playerReferences.InputHandler.CreativeMenuTogglePressed -= CloseFromOtherScreen;

            if (inventory != null)
                inventory.InventoryChanged -= OnInventoryChanged;
        }

        private void Update()
        {
            if (!IsOpen)
                return;

            UIInputFocus.EnforceCursor();

            // Station proximity and craft progress change without inventory
            // events, so the detail panel refreshes on a short timer.
            if (craftingSystem.IsCrafting || Time.unscaledTime >= nextStationRefresh)
            {
                nextStationRefresh = Time.unscaledTime + StationRefreshInterval;
                RefreshDetail();
            }
        }

        public void Toggle()
        {
            if (IsOpen)
            {
                Close();
                return;
            }

            EnsureInitialized();

            if (inventoryUI != null && inventoryUI.IsOpen)
                inventoryUI.Toggle();
            // The creative menu closes itself on other screens' toggle keys.

            IsOpen = true;
            panel.SetActive(true);
            UIInputFocus.Acquire(playerReferences.InputHandler);
            inventory.InventoryChanged += OnInventoryChanged;
            RebuildRecipeList();
            RefreshDetail();
        }

        private void Close()
        {
            if (!IsOpen)
                return;

            IsOpen = false;
            panel.SetActive(false);
            UIInputFocus.Release(playerReferences.InputHandler);
            inventory.InventoryChanged -= OnInventoryChanged;
        }

        private void CloseFromOtherScreen()
        {
            Close();
        }

        // ------------------------------------------------------------------
        // List
        // ------------------------------------------------------------------

        private void EnsureInitialized()
        {
            if (initialized)
                return;

            initialized = true;
            recipeDatabase = Resources.Load<RecipeDatabase>(RecipeDatabase.ResourcesPath);
            if (recipeDatabase == null)
                Debug.LogWarning("CraftingMenuController: RecipeDatabase missing — run Island Game/Data/Sync Databases.");

            if (placementController == null)
                placementController = FindFirstObjectByType<BuildingPlacementController>();

            recipeEntryTemplate.SetActive(false);
            ingredientRowTemplate.SetActive(false);

            // Optional on purpose: a canvas built before Phase 3 has no tab
            // buttons and simply shows every recipe in one list.
            if (tabAllButton != null)
                tabAllButton.onClick.AddListener(() => SelectTab(RecipeTab.All));
            if (tabItemsButton != null)
                tabItemsButton.onClick.AddListener(() => SelectTab(RecipeTab.Items));
            if (tabBuildingButton != null)
                tabBuildingButton.onClick.AddListener(() => SelectTab(RecipeTab.Building));
            UpdateTabVisuals();

            searchField.onValueChanged.AddListener(value =>
            {
                search = value ?? string.Empty;
                RebuildRecipeList();
            });

            craftableOnlyToggle.onValueChanged.AddListener(value =>
            {
                craftableOnly = value;
                RebuildRecipeList();
            });

            craftButton.onClick.AddListener(OnCraftClicked);
        }

        private void OnInventoryChanged()
        {
            RebuildRecipeList();
            RefreshDetail();
        }

        private void RebuildRecipeList()
        {
            if (recipeDatabase == null)
                return;

            int used = 0;
            foreach (RecipeDefinition recipe in recipeDatabase.All)
            {
                if (recipe == null || !PassesTab(recipe) || !PassesSearch(recipe))
                    continue;

                bool hasIngredients = craftingSystem.HasIngredientsFor(recipe);
                if (craftableOnly && !hasIngredients)
                    continue;

                RecipeListEntryView view = GetPooledEntry(used);
                view.gameObject.SetActive(true);
                view.Bind(this, recipe, hasIngredients, recipe == selectedRecipe);
                used++;
            }

            for (int i = used; i < entryPool.Count; i++)
                entryPool[i].gameObject.SetActive(false);
        }

        private RecipeListEntryView GetPooledEntry(int index)
        {
            if (index < entryPool.Count)
                return entryPool[index];

            GameObject entry = Instantiate(recipeEntryTemplate, recipeEntryTemplate.transform.parent);
            entry.name = $"Recipe_{index}";
            var view = entry.GetComponent<RecipeListEntryView>();
            entryPool.Add(view);
            return view;
        }

        private void SelectTab(RecipeTab newTab)
        {
            if (tab == newTab)
                return;

            tab = newTab;
            UpdateTabVisuals();
            RebuildRecipeList();
        }

        private void UpdateTabVisuals()
        {
            SetTabColor(tabAllButton, tab == RecipeTab.All);
            SetTabColor(tabItemsButton, tab == RecipeTab.Items);
            SetTabColor(tabBuildingButton, tab == RecipeTab.Building);
        }

        private static void SetTabColor(Button button, bool active)
        {
            if (button != null && button.targetGraphic != null)
                button.targetGraphic.color = active ? ActiveTabColor : InactiveTabColor;
        }

        private bool PassesTab(RecipeDefinition recipe)
        {
            switch (tab)
            {
                case RecipeTab.Items: return !recipe.IsBuildingRecipe;
                case RecipeTab.Building: return recipe.IsBuildingRecipe;
                default: return true;
            }
        }

        private bool PassesSearch(RecipeDefinition recipe)
        {
            if (string.IsNullOrWhiteSpace(search))
                return true;

            string needle = search.Trim();
            return Contains(recipe.DisplayName, needle)
                   || Contains(recipe.Id, needle)
                   || (recipe.Output != null && Contains(recipe.Output.DisplayName, needle))
                   || (recipe.OutputPiece != null && Contains(recipe.OutputPiece.DisplayName, needle));
        }

        private static bool Contains(string text, string needle)
        {
            return !string.IsNullOrEmpty(text)
                   && text.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ------------------------------------------------------------------
        // Detail
        // ------------------------------------------------------------------

        public void SelectRecipe(RecipeDefinition recipe)
        {
            selectedRecipe = recipe;
            RebuildRecipeList(); // selection tint
            RefreshDetail();
        }

        private void RefreshDetail()
        {
            if (selectedRecipe == null)
            {
                outputIcon.enabled = false;
                outputName.text = "Select a recipe.";
                stationLine.text = string.Empty;
                timeLine.text = string.Empty;
                reasonText.text = string.Empty;
                craftButton.interactable = false;
                craftButtonLabel.text = "Craft";
                progressFill.fillAmount = 0f;
                BindIngredientRows(null);
                return;
            }

            RecipeDefinition recipe = selectedRecipe;

            Sprite icon = recipe.Output != null ? recipe.Output.Icon
                : recipe.OutputPiece != null ? recipe.OutputPiece.Icon : null;
            outputIcon.enabled = icon != null;
            outputIcon.sprite = icon;

            string displayName = string.IsNullOrEmpty(recipe.DisplayName) ? recipe.name : recipe.DisplayName;
            outputName.text = !recipe.IsBuildingRecipe && recipe.OutputCount > 1
                ? $"{displayName}  ×{recipe.OutputCount}"
                : displayName;

            if (recipe.Station == CraftingStationType.None)
            {
                stationLine.text = "Crafted by hand";
                stationLine.color = Color.white;
            }
            else if (craftingSystem.IsNearStation(recipe.Station))
            {
                stationLine.text = $"At {recipe.Station} ✓";
                stationLine.color = new Color(0.45f, 0.95f, 0.45f);
            }
            else
            {
                stationLine.text = $"Requires {recipe.Station} — none nearby";
                stationLine.color = new Color(1f, 0.4f, 0.35f);
            }

            timeLine.text = recipe.IsBuildingRecipe
                ? "Placed in the world — aim and click to build"
                : recipe.CraftSeconds <= 0f ? "Instant" : $"Takes {recipe.CraftSeconds:0.#} s";

            BindIngredientRows(recipe);

            if (recipe.IsBuildingRecipe)
            {
                // Build arms the placement ghost; ingredients are checked and
                // consumed there, on confirm. Missing items show as info here
                // but never block arming — the red ghost carries the message.
                craftButton.interactable = !recipe.HasDanglingReferences && placementController != null;
                craftButtonLabel.text = "Build";
                progressFill.fillAmount = 0f;

                if (placementController == null)
                    reasonText.text = "No BuildingPlacementController on the player — run Island Game/World/Add Building System To Player.";
                else
                    reasonText.text = craftingSystem.ValidateRequirements(recipe, out string buildReason)
                        ? string.Empty
                        : buildReason;
                return;
            }

            bool craftingThis = craftingSystem.IsCrafting && craftingSystem.ActiveRecipe == recipe;
            if (craftingThis)
            {
                craftButton.interactable = false;
                craftButtonLabel.text = $"Crafting… {Mathf.RoundToInt(craftingSystem.Progress01 * 100f)}%";
                progressFill.fillAmount = craftingSystem.Progress01;
                reasonText.text = string.Empty;
            }
            else
            {
                bool canCraft = craftingSystem.CanCraft(recipe, out string reason);
                craftButton.interactable = canCraft;
                craftButtonLabel.text = "Craft";
                progressFill.fillAmount = 0f;
                reasonText.text = canCraft ? string.Empty : reason;
            }
        }

        private void BindIngredientRows(RecipeDefinition recipe)
        {
            int used = 0;
            if (recipe != null)
            {
                foreach (RecipeIngredient ingredient in recipe.Ingredients)
                {
                    if (ingredient == null || ingredient.Item == null)
                        continue; // dangling rows are reported by CanCraft's reason

                    IngredientRowView row = GetPooledRow(used);
                    row.gameObject.SetActive(true);
                    row.Bind(ingredient.Item.DisplayName, inventory.GetItemCount(ingredient.Item), ingredient.Count);
                    used++;
                }
            }

            for (int i = used; i < rowPool.Count; i++)
                rowPool[i].gameObject.SetActive(false);
        }

        private IngredientRowView GetPooledRow(int index)
        {
            if (index < rowPool.Count)
                return rowPool[index];

            GameObject row = Instantiate(ingredientRowTemplate, ingredientRowTemplate.transform.parent);
            row.name = $"Ingredient_{index}";
            var view = row.GetComponent<IngredientRowView>();
            rowPool.Add(view);
            return view;
        }

        private void OnCraftClicked()
        {
            if (selectedRecipe == null)
                return;

            if (selectedRecipe.IsBuildingRecipe)
            {
                if (placementController == null || selectedRecipe.OutputPiece == null)
                    return;

                // Arm and close: the player is now aiming the ghost, which
                // enforces (and consumes) this recipe's cost on placement.
                placementController.ArmPiece(selectedRecipe.OutputPiece);
                Close();
                return;
            }

            craftingSystem.TryStartCraft(selectedRecipe, out _);
            RefreshDetail(); // instant crafts show results immediately; failures surface LastError via CanCraft
        }
    }
}
