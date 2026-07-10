using IslandGame.Inventory;
using IslandGame.Inventory.UI;
using IslandGame.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace IslandGame.EditorTools
{
    /// <summary>
    /// One-click scene setup for the Phase 4 inventory (same builder workflow
    /// as PlayerRigBuilder): ensures the player has InventorySystem,
    /// HotbarSelector and ItemPickupCollector, ensures an EventSystem with the
    /// Input System UI module, then constructs the uGUI hierarchy — hotbar,
    /// backpack panel, tooltip, drag ghost — and wires every serialized
    /// reference. Idempotent: refuses to build a second InventoryCanvas.
    /// The result is plain scene objects; restyle them freely, the views only
    /// care about the wired references.
    /// </summary>
    public static class InventoryUIBuilder
    {
        private const string CanvasName = "InventoryCanvas";
        private const int SlotSize = 64;
        private const int SlotSpacing = 6;

        private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.78f);
        private static readonly Color SlotColor = new Color(0.12f, 0.12f, 0.14f, 0.9f);
        private static readonly Color SelectionColor = new Color(1f, 0.85f, 0.3f, 0.3f);
        private static readonly Color TooltipColor = new Color(0.08f, 0.08f, 0.1f, 0.95f);

        [MenuItem("Island Game/UI/Build Inventory UI")]
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

            InventorySystem inventory = EnsurePlayerComponent<InventorySystem>(playerReferences.gameObject);
            HotbarSelector selector = EnsurePlayerComponent<HotbarSelector>(playerReferences.gameObject);
            EnsurePlayerComponent<ItemPickupCollector>(playerReferences.gameObject);
            EnsureEventSystem();

            // --- Canvas root --------------------------------------------
            var canvasObject = new GameObject(CanvasName);
            Undo.RegisterCreatedObjectUndo(canvasObject, "Build Inventory UI");
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasObject.AddComponent<GraphicRaycaster>();
            var controller = canvasObject.AddComponent<InventoryUIController>();

            // --- Hotbar --------------------------------------------------
            RectTransform hotbar = CreateRect("Hotbar", canvasObject.transform);
            hotbar.anchorMin = hotbar.anchorMax = new Vector2(0.5f, 0f);
            hotbar.pivot = new Vector2(0.5f, 0f);
            hotbar.anchoredPosition = new Vector2(0f, 12f);
            hotbar.sizeDelta = new Vector2(9 * SlotSize + 8 * SlotSpacing + 16, SlotSize + 16);
            hotbar.gameObject.AddComponent<Image>().color = PanelColor;
            var hotbarLayout = hotbar.gameObject.AddComponent<HorizontalLayoutGroup>();
            ConfigureLayout(hotbarLayout);
            var hotbarView = hotbar.gameObject.AddComponent<HotbarView>();

            GameObject hotbarTemplate = BuildSlotTemplate(hotbar);

            // --- Backpack panel ------------------------------------------
            RectTransform panel = CreateRect("InventoryPanel", canvasObject.transform);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = new Vector2(0f, 60f);
            panel.sizeDelta = new Vector2(9 * (SlotSize + SlotSpacing) + 26, 3 * (SlotSize + SlotSpacing) + 80);
            panel.gameObject.AddComponent<Image>().color = PanelColor;
            var gridView = panel.gameObject.AddComponent<InventoryGridView>();

            Text title = CreateText(panel, "Title", "Inventory", 20, TextAnchor.MiddleLeft, FontStyle.Bold);
            SetTopStrip(title.rectTransform, left: true);

            Text weightLabel = CreateText(panel, "WeightLabel", "0.0 / 0.0 kg", 16, TextAnchor.MiddleRight, FontStyle.Normal);
            SetTopStrip(weightLabel.rectTransform, left: false);

            RectTransform grid = CreateRect("Grid", panel);
            grid.anchorMin = new Vector2(0f, 0f);
            grid.anchorMax = new Vector2(1f, 1f);
            grid.offsetMin = new Vector2(13f, 13f);
            grid.offsetMax = new Vector2(-13f, -44f);
            var gridLayout = grid.gameObject.AddComponent<GridLayoutGroup>();
            gridLayout.cellSize = new Vector2(SlotSize, SlotSize);
            gridLayout.spacing = new Vector2(SlotSpacing, SlotSpacing);
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = 9;

            GameObject gridTemplate = BuildSlotTemplate(grid);

            // --- Tooltip --------------------------------------------------
            RectTransform tooltipRect = CreateRect("Tooltip", canvasObject.transform);
            tooltipRect.pivot = new Vector2(0f, 1f);
            tooltipRect.sizeDelta = new Vector2(280f, 120f);
            tooltipRect.gameObject.AddComponent<Image>().color = TooltipColor;
            tooltipRect.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
            var tooltip = tooltipRect.gameObject.AddComponent<InventoryTooltip>();

            Text tooltipName = CreateText(tooltipRect, "Name", "Item", 18, TextAnchor.UpperLeft, FontStyle.Bold);
            Stretch(tooltipName.rectTransform, 10f, 10f, 10f, 34f);
            tooltipName.rectTransform.offsetMax = new Vector2(-10f, -8f);

            Text tooltipBody = CreateText(tooltipRect, "Body", "Description", 14, TextAnchor.UpperLeft, FontStyle.Normal);
            Stretch(tooltipBody.rectTransform, 10f, 10f, 34f, 8f);

            // --- Drag ghost ----------------------------------------------
            RectTransform ghost = CreateRect("DragGhost", canvasObject.transform);
            ghost.sizeDelta = new Vector2(SlotSize - 8, SlotSize - 8);
            var ghostIcon = ghost.gameObject.AddComponent<Image>();
            ghostIcon.raycastTarget = false;
            ghost.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
            Text ghostCount = CreateText(ghost, "Count", "", 16, TextAnchor.LowerRight, FontStyle.Bold);
            Stretch(ghostCount.rectTransform, 2f, 2f, 2f, 2f);
            ghostCount.raycastTarget = false;

            // --- Wire everything -----------------------------------------
            Wire(controller, ("inventory", inventory), ("playerReferences", playerReferences),
                ("inventoryPanel", panel.gameObject), ("tooltip", tooltip),
                ("dragGhost", ghost), ("dragGhostIcon", ghostIcon), ("dragGhostCount", ghostCount));
            Wire(hotbarView, ("controller", controller), ("selector", selector), ("slotTemplate", hotbarTemplate));
            Wire(gridView, ("controller", controller), ("slotTemplate", gridTemplate), ("weightLabel", weightLabel));
            Wire(tooltip, ("nameText", tooltipName), ("bodyText", tooltipBody));

            EditorSceneManager.MarkSceneDirty(canvasObject.scene);
            Selection.activeGameObject = canvasObject;
            Debug.Log("Inventory UI built: canvas + hotbar + backpack panel + tooltip + drag ghost, player components ensured and wired. Save the scene.");
        }

        // ------------------------------------------------------------------
        // Pieces
        // ------------------------------------------------------------------

        private static GameObject BuildSlotTemplate(Transform parent)
        {
            RectTransform root = CreateRect("SlotTemplate", parent);
            root.sizeDelta = new Vector2(SlotSize, SlotSize);
            var background = root.gameObject.AddComponent<Image>();
            background.color = SlotColor; // raycast target ON — the slot IS the pointer surface

            var view = root.gameObject.AddComponent<InventorySlotView>();

            RectTransform selection = CreateRect("Selection", root);
            Stretch(selection, 0f, 0f, 0f, 0f);
            var selectionImage = selection.gameObject.AddComponent<Image>();
            selectionImage.color = SelectionColor;
            selectionImage.raycastTarget = false;
            selectionImage.enabled = false;

            RectTransform icon = CreateRect("Icon", root);
            Stretch(icon, 6f, 6f, 6f, 6f);
            var iconImage = icon.gameObject.AddComponent<Image>();
            iconImage.raycastTarget = false;
            iconImage.preserveAspect = true;
            iconImage.enabled = false;

            Text count = CreateText(root, "Count", "", 16, TextAnchor.LowerRight, FontStyle.Bold);
            Stretch(count.rectTransform, 4f, 4f, 4f, 4f);
            count.raycastTarget = false;

            Wire(view, ("icon", iconImage), ("countText", count), ("selectionHighlight", selectionImage));

            root.gameObject.SetActive(false);
            return root.gameObject;
        }

        private static void EnsureEventSystem()
        {
            var eventSystem = Object.FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                var eventSystemObject = new GameObject("EventSystem");
                Undo.RegisterCreatedObjectUndo(eventSystemObject, "Build Inventory UI");
                eventSystem = eventSystemObject.AddComponent<EventSystem>();
            }

            // The project runs the new Input System; the legacy module would sit dead.
            var legacyModule = eventSystem.GetComponent<StandaloneInputModule>();
            if (legacyModule != null)
                Undo.DestroyObjectImmediate(legacyModule);
            if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
                Undo.AddComponent<InputSystemUIInputModule>(eventSystem.gameObject);
        }

        private static T EnsurePlayerComponent<T>(GameObject player) where T : Component
        {
            var component = player.GetComponent<T>();
            if (component == null)
            {
                component = Undo.AddComponent<T>(player);
                Debug.Log($"Added {typeof(T).Name} to '{player.name}'.", player);
            }

            return component;
        }

        // ------------------------------------------------------------------
        // uGUI helpers
        // ------------------------------------------------------------------

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

        private static void ConfigureLayout(HorizontalLayoutGroup layout)
        {
            layout.spacing = SlotSpacing;
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
        }

        private static void Stretch(RectTransform rect, float left, float right, float bottom, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void SetTopStrip(RectTransform rect, bool left)
        {
            rect.anchorMin = new Vector2(left ? 0f : 0.5f, 1f);
            rect.anchorMax = new Vector2(left ? 0.5f : 1f, 1f);
            rect.pivot = new Vector2(left ? 0f : 1f, 1f);
            rect.anchoredPosition = new Vector2(left ? 14f : -14f, -8f);
            rect.sizeDelta = new Vector2(0f, 28f);
        }

        private static void Wire(Component target, params (string field, Object value)[] assignments)
        {
            var serialized = new SerializedObject(target);
            foreach ((string field, Object value) in assignments)
            {
                SerializedProperty property = serialized.FindProperty(field);
                if (property == null)
                {
                    Debug.LogError($"InventoryUIBuilder: no serialized field '{field}' on {target.GetType().Name}.");
                    continue;
                }

                property.objectReferenceValue = value;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
