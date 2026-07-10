using IslandGame.Crafting;
using IslandGame.Crafting.UI;
using IslandGame.Creative.UI;
using IslandGame.Inventory;
using IslandGame.Inventory.UI;
using IslandGame.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace IslandGame.EditorTools
{
    /// <summary>
    /// One-click scene setup for the Phase 8 crafting menu, following the
    /// inventory/creative builder workflow: ensures CraftingSystem on the
    /// player, builds the CraftingMenuCanvas (recipe list with search +
    /// craftable-only toggle, detail panel with ingredient rows, craft button
    /// with progress fill) and wires every reference — including the optional
    /// cross-links to the inventory and creative screens for mutual exclusion.
    /// Idempotent; restyle the produced objects freely.
    /// </summary>
    public static class CraftingMenuUIBuilder
    {
        private const string CanvasName = "CraftingMenuCanvas";

        private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.82f);
        private static readonly Color WidgetColor = new Color(0.12f, 0.12f, 0.14f, 0.9f);

        [MenuItem("Island Game/UI/Build Crafting Menu UI")]
        public static void Build()
        {
            if (GameObject.Find(CanvasName) != null)
            {
                Debug.LogWarning($"'{CanvasName}' already exists in the scene — delete it first to rebuild.");
                Selection.activeGameObject = GameObject.Find(CanvasName);
                return;
            }

            var playerReferences = Object.FindFirstObjectByType<PlayerReferences>();
            if (playerReferences == null)
            {
                Debug.LogError("No PlayerReferences found in the open scene — open the gameplay scene with the player first.");
                return;
            }

            var inventory = playerReferences.GetComponent<InventorySystem>();
            if (inventory == null)
            {
                Debug.LogError("Player has no InventorySystem — run Island Game/UI/Build Inventory UI first.");
                return;
            }

            CraftingSystem craftingSystem = playerReferences.GetComponent<CraftingSystem>();
            if (craftingSystem == null)
            {
                craftingSystem = Undo.AddComponent<CraftingSystem>(playerReferences.gameObject);
                Debug.Log($"Added CraftingSystem to '{playerReferences.name}'.", playerReferences);
            }

            // --- Canvas root --------------------------------------------
            var canvasObject = new GameObject(CanvasName);
            Undo.RegisterCreatedObjectUndo(canvasObject, "Build Crafting Menu UI");
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasObject.AddComponent<GraphicRaycaster>();
            var controller = canvasObject.AddComponent<CraftingMenuController>();

            // --- Panel ----------------------------------------------------
            RectTransform panel = CreateRect("Panel", canvasObject.transform);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(840f, 520f);
            panel.gameObject.AddComponent<Image>().color = PanelColor;

            Text title = CreateText(panel, "Title", "Crafting", 20, TextAnchor.MiddleLeft, FontStyle.Bold);
            Place(title.rectTransform, 14f, -8f, 200f, 28f);

            // --- Left column: tabs / search / filter / recipe list --------
            Button tabAll = BuildTabButton(panel, "TabAll", "All");
            Place(((RectTransform)tabAll.transform), 13f, -44f, 96f, 24f);
            Button tabItems = BuildTabButton(panel, "TabItems", "Items");
            Place(((RectTransform)tabItems.transform), 115f, -44f, 96f, 24f);
            Button tabBuilding = BuildTabButton(panel, "TabBuilding", "Building");
            Place(((RectTransform)tabBuilding.transform), 217f, -44f, 96f, 24f);

            InputField searchField = BuildInputField(panel, "SearchField", "Search recipes…");
            Place(((RectTransform)searchField.transform), 13f, -74f, 300f, 26f);

            Toggle craftableToggle = BuildToggle(panel, "CraftableOnlyToggle", "Craftable only");
            Place(((RectTransform)craftableToggle.transform), 13f, -106f, 300f, 20f);

            (ScrollRect recipeScroll, RectTransform recipeContent) = BuildScrollList(panel, "RecipeList");
            RectTransform scrollRect = (RectTransform)recipeScroll.transform;
            scrollRect.anchorMin = new Vector2(0f, 0f);
            scrollRect.anchorMax = new Vector2(0f, 1f);
            scrollRect.pivot = new Vector2(0f, 1f);
            scrollRect.anchoredPosition = new Vector2(13f, -132f);
            scrollRect.sizeDelta = new Vector2(300f, -145f);

            GameObject recipeTemplate = BuildRecipeEntryTemplate(recipeContent);

            // --- Right column: detail -------------------------------------
            RectTransform detail = CreateRect("Detail", panel);
            detail.anchorMin = new Vector2(0f, 0f);
            detail.anchorMax = new Vector2(1f, 1f);
            detail.offsetMin = new Vector2(330f, 13f);
            detail.offsetMax = new Vector2(-13f, -44f);

            RectTransform outputIconRect = CreateRect("OutputIcon", detail);
            outputIconRect.anchorMin = outputIconRect.anchorMax = outputIconRect.pivot = new Vector2(0f, 1f);
            outputIconRect.anchoredPosition = new Vector2(0f, 0f);
            outputIconRect.sizeDelta = new Vector2(56f, 56f);
            var outputIcon = outputIconRect.gameObject.AddComponent<Image>();
            outputIcon.preserveAspect = true;
            outputIcon.enabled = false;

            Text outputName = CreateText(detail, "OutputName", "Select a recipe.", 19, TextAnchor.MiddleLeft, FontStyle.Bold);
            Place(outputName.rectTransform, 66f, 0f, 380f, 30f);

            Text stationLine = CreateText(detail, "StationLine", "", 14, TextAnchor.MiddleLeft, FontStyle.Normal);
            Place(stationLine.rectTransform, 66f, -30f, 380f, 20f);

            Text timeLine = CreateText(detail, "TimeLine", "", 14, TextAnchor.MiddleLeft, FontStyle.Normal);
            Place(timeLine.rectTransform, 66f, -50f, 380f, 20f);

            Text ingredientsHeader = CreateText(detail, "IngredientsHeader", "Ingredients", 15, TextAnchor.MiddleLeft, FontStyle.Bold);
            Place(ingredientsHeader.rectTransform, 0f, -86f, 250f, 22f);

            RectTransform ingredientContainer = CreateRect("IngredientRows", detail);
            ingredientContainer.anchorMin = new Vector2(0f, 0f);
            ingredientContainer.anchorMax = new Vector2(1f, 1f);
            ingredientContainer.offsetMin = new Vector2(0f, 120f);
            ingredientContainer.offsetMax = new Vector2(0f, -110f);
            var rowLayout = ingredientContainer.gameObject.AddComponent<VerticalLayoutGroup>();
            rowLayout.spacing = 4f;
            rowLayout.childAlignment = TextAnchor.UpperLeft;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = false;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = false;

            GameObject ingredientTemplate = BuildIngredientRowTemplate(ingredientContainer);

            Text reasonText = CreateText(detail, "ReasonText", "", 13, TextAnchor.UpperLeft, FontStyle.Normal);
            reasonText.color = new Color(1f, 0.4f, 0.35f);
            reasonText.rectTransform.anchorMin = new Vector2(0f, 0f);
            reasonText.rectTransform.anchorMax = new Vector2(1f, 0f);
            reasonText.rectTransform.pivot = new Vector2(0f, 0f);
            reasonText.rectTransform.anchoredPosition = new Vector2(0f, 66f);
            reasonText.rectTransform.sizeDelta = new Vector2(-180f, 46f);

            RectTransform progressRect = CreateRect("Progress", detail);
            progressRect.anchorMin = new Vector2(0f, 0f);
            progressRect.anchorMax = new Vector2(1f, 0f);
            progressRect.pivot = new Vector2(0f, 0f);
            progressRect.anchoredPosition = new Vector2(0f, 48f);
            progressRect.sizeDelta = new Vector2(0f, 8f);
            progressRect.gameObject.AddComponent<Image>().color = WidgetColor;

            RectTransform fillRect = CreateRect("Fill", progressRect);
            Stretch(fillRect, 1f, 1f, 1f, 1f);
            var progressFill = fillRect.gameObject.AddComponent<Image>();
            progressFill.color = new Color(0.45f, 0.85f, 0.45f);
            progressFill.type = Image.Type.Filled;
            progressFill.fillMethod = Image.FillMethod.Horizontal;
            progressFill.fillAmount = 0f;

            RectTransform craftRect = CreateRect("CraftButton", detail);
            craftRect.anchorMin = craftRect.anchorMax = craftRect.pivot = new Vector2(1f, 0f);
            craftRect.anchoredPosition = new Vector2(0f, 0f);
            craftRect.sizeDelta = new Vector2(170f, 38f);
            var craftImage = craftRect.gameObject.AddComponent<Image>();
            craftImage.color = new Color(0.2f, 0.42f, 0.2f, 1f);
            var craftButton = craftRect.gameObject.AddComponent<Button>();
            craftButton.targetGraphic = craftImage;
            Text craftLabel = CreateText(craftRect, "Label", "Craft", 17, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(craftLabel.rectTransform, 0f, 0f, 0f, 0f);

            // --- Wire everything -----------------------------------------
            Wire(controller,
                ("playerReferences", playerReferences), ("inventory", inventory), ("craftingSystem", craftingSystem),
                ("inventoryUI", Object.FindFirstObjectByType<InventoryUIController>()),
                ("creativeMenu", Object.FindFirstObjectByType<CreativeMenuController>()),
                ("placementController", Object.FindFirstObjectByType<IslandGame.Building.BuildingPlacementController>()),
                ("panel", panel.gameObject), ("searchField", searchField), ("craftableOnlyToggle", craftableToggle),
                ("tabAllButton", tabAll), ("tabItemsButton", tabItems), ("tabBuildingButton", tabBuilding),
                ("recipeEntryTemplate", recipeTemplate), ("recipeScroll", recipeScroll),
                ("outputIcon", outputIcon), ("outputName", outputName), ("stationLine", stationLine),
                ("timeLine", timeLine), ("ingredientRowTemplate", ingredientTemplate),
                ("craftButton", craftButton), ("craftButtonLabel", craftLabel),
                ("progressFill", progressFill), ("reasonText", reasonText));

            EditorSceneManager.MarkSceneDirty(canvasObject.scene);
            Selection.activeGameObject = canvasObject;
            Debug.Log("Crafting menu UI built and wired (toggle key: B). Save the scene.");
        }

        // ------------------------------------------------------------------
        // Templates
        // ------------------------------------------------------------------

        private static GameObject BuildRecipeEntryTemplate(RectTransform parent)
        {
            RectTransform root = CreateRect("RecipeEntryTemplate", parent);
            root.sizeDelta = new Vector2(280f, 32f);
            var background = root.gameObject.AddComponent<Image>();
            background.color = WidgetColor;
            var button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            var layout = root.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 32f;
            layout.preferredHeight = 32f;

            RectTransform iconRect = CreateRect("Icon", root);
            iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new Vector2(6f, 0f);
            iconRect.sizeDelta = new Vector2(24f, 24f);
            var icon = iconRect.gameObject.AddComponent<Image>();
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            icon.enabled = false;

            Text label = CreateText(root, "Label", "Recipe", 14, TextAnchor.MiddleLeft, FontStyle.Normal);
            Stretch(label.rectTransform, 38f, 6f, 2f, 2f);
            label.raycastTarget = false;

            var view = root.gameObject.AddComponent<RecipeListEntryView>();
            Wire(view, ("background", background), ("icon", icon), ("label", label));

            root.gameObject.SetActive(false);
            return root.gameObject;
        }

        private static GameObject BuildIngredientRowTemplate(RectTransform parent)
        {
            RectTransform root = CreateRect("IngredientRowTemplate", parent);
            root.sizeDelta = new Vector2(0f, 22f);
            var layout = root.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 22f;
            layout.preferredHeight = 22f;

            Text nameLabel = CreateText(root, "Name", "Item", 14, TextAnchor.MiddleLeft, FontStyle.Normal);
            Stretch(nameLabel.rectTransform, 4f, 90f, 0f, 0f);

            Text countLabel = CreateText(root, "Count", "0/0", 14, TextAnchor.MiddleRight, FontStyle.Bold);
            countLabel.rectTransform.anchorMin = new Vector2(1f, 0f);
            countLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            countLabel.rectTransform.pivot = new Vector2(1f, 0.5f);
            countLabel.rectTransform.anchoredPosition = new Vector2(-4f, 0f);
            countLabel.rectTransform.sizeDelta = new Vector2(80f, 0f);

            var view = root.gameObject.AddComponent<IngredientRowView>();
            Wire(view, ("nameLabel", nameLabel), ("countLabel", countLabel));

            root.gameObject.SetActive(false);
            return root.gameObject;
        }

        // ------------------------------------------------------------------
        // uGUI widget helpers
        // ------------------------------------------------------------------

        private static Button BuildTabButton(RectTransform parent, string name, string labelText)
        {
            RectTransform root = CreateRect(name, parent);
            var background = root.gameObject.AddComponent<Image>();
            background.color = WidgetColor; // the controller tints the active tab
            var button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = background;

            Text label = CreateText(root, "Label", labelText, 14, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(label.rectTransform, 2f, 2f, 2f, 2f);
            label.raycastTarget = false;
            return button;
        }

        private static InputField BuildInputField(RectTransform parent, string name, string placeholderText)
        {
            RectTransform root = CreateRect(name, parent);
            var background = root.gameObject.AddComponent<Image>();
            background.color = WidgetColor;
            var field = root.gameObject.AddComponent<InputField>();
            field.targetGraphic = background;

            Text text = CreateText(root, "Text", "", 14, TextAnchor.MiddleLeft, FontStyle.Normal);
            Stretch(text.rectTransform, 8f, 8f, 2f, 2f);
            text.supportRichText = false;

            Text placeholder = CreateText(root, "Placeholder", placeholderText, 14, TextAnchor.MiddleLeft, FontStyle.Italic);
            Stretch(placeholder.rectTransform, 8f, 8f, 2f, 2f);
            placeholder.color = new Color(1f, 1f, 1f, 0.35f);

            field.textComponent = text;
            field.placeholder = placeholder;
            return field;
        }

        private static Toggle BuildToggle(RectTransform parent, string name, string labelText)
        {
            RectTransform root = CreateRect(name, parent);
            var toggle = root.gameObject.AddComponent<Toggle>();

            RectTransform boxRect = CreateRect("Background", root);
            boxRect.anchorMin = boxRect.anchorMax = boxRect.pivot = new Vector2(0f, 0.5f);
            boxRect.anchoredPosition = new Vector2(0f, 0f);
            boxRect.sizeDelta = new Vector2(18f, 18f);
            var box = boxRect.gameObject.AddComponent<Image>();
            box.color = WidgetColor;

            RectTransform checkRect = CreateRect("Checkmark", boxRect);
            Stretch(checkRect, 3f, 3f, 3f, 3f);
            var check = checkRect.gameObject.AddComponent<Image>();
            check.color = new Color(0.45f, 0.85f, 0.45f);

            Text label = CreateText(root, "Label", labelText, 14, TextAnchor.MiddleLeft, FontStyle.Normal);
            Stretch(label.rectTransform, 24f, 0f, 0f, 0f);
            label.raycastTarget = false;

            toggle.targetGraphic = box;
            toggle.graphic = check;
            toggle.isOn = false;
            return toggle;
        }

        private static (ScrollRect, RectTransform) BuildScrollList(RectTransform parent, string name)
        {
            RectTransform root = CreateRect(name, parent);
            root.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);
            var scroll = root.gameObject.AddComponent<ScrollRect>();

            RectTransform viewport = CreateRect("Viewport", root);
            Stretch(viewport, 2f, 2f, 2f, 2f);
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = CreateRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, 0f);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 3f;
            layout.padding = new RectOffset(3, 3, 3, 3);
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 25f;
            return (scroll, content);
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)gameObject.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static Text CreateText(
            Transform parent, string name, string content, int fontSize, TextAnchor alignment, FontStyle style)
        {
            RectTransform rect = CreateRect(name, parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        /// <summary>Anchors a rect to the parent's top-left with the given offset/size.</summary>
        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void Stretch(RectTransform rect, float left, float right, float bottom, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void Wire(Component target, params (string field, Object value)[] assignments)
        {
            var serialized = new SerializedObject(target);
            foreach ((string field, Object value) in assignments)
            {
                SerializedProperty property = serialized.FindProperty(field);
                if (property == null)
                {
                    Debug.LogError($"CraftingMenuUIBuilder: no serialized field '{field}' on {target.GetType().Name}.");
                    continue;
                }

                property.objectReferenceValue = value;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
