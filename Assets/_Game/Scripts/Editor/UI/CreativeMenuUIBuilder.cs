using IslandGame.Creative;
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
    /// One-click scene setup for the Phase 5 creative menu, mirroring
    /// InventoryUIBuilder: ensures CreativeModeController on the player, then
    /// builds the CreativeMenuCanvas (panel, search field, tab row, scrollable
    /// entry grid, toast line) and wires every reference — including the
    /// optional cross-link to the inventory UI so the two screens never stack.
    /// Idempotent: refuses to build a second CreativeMenuCanvas. Run the
    /// inventory builder FIRST (this one needs InventorySystem and, ideally,
    /// the InventoryUIController cross-link).
    /// </summary>
    public static class CreativeMenuUIBuilder
    {
        private const string CanvasName = "CreativeMenuCanvas";
        private const int EntrySize = 88;
        private const int Spacing = 6;

        private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.82f);
        private static readonly Color CellColor = new Color(0.12f, 0.12f, 0.14f, 0.9f);
        private static readonly Color FieldColor = new Color(0.08f, 0.08f, 0.1f, 1f);

        [MenuItem("Island Game/UI/Build Creative Menu UI")]
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

            CreativeModeController creativeMode = EnsureComponent<CreativeModeController>(playerReferences.gameObject);
            var inventoryUI = Object.FindFirstObjectByType<InventoryUIController>();
            if (inventoryUI == null)
                Debug.LogWarning("No InventoryUIController found — the creative menu will work, but the two screens won't auto-close each other.");

            // --- Canvas root ---------------------------------------------
            var canvasObject = new GameObject(CanvasName);
            Undo.RegisterCreatedObjectUndo(canvasObject, "Build Creative Menu UI");
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20; // above the inventory canvas
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasObject.AddComponent<GraphicRaycaster>();
            var controller = canvasObject.AddComponent<CreativeMenuController>();

            // --- Panel ----------------------------------------------------
            RectTransform panel = CreateRect("Panel", canvasObject.transform);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(860f, 560f);
            panel.gameObject.AddComponent<Image>().color = PanelColor;

            Text title = CreateText(panel, "Title", "Creative", 20, TextAnchor.MiddleLeft, FontStyle.Bold);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(0.4f, 1f);
            title.rectTransform.pivot = new Vector2(0f, 1f);
            title.rectTransform.anchoredPosition = new Vector2(14f, -8f);
            title.rectTransform.sizeDelta = new Vector2(0f, 28f);

            // --- Search field ---------------------------------------------
            RectTransform searchRect = CreateRect("SearchField", panel);
            searchRect.anchorMin = new Vector2(0f, 1f);
            searchRect.anchorMax = new Vector2(1f, 1f);
            searchRect.pivot = new Vector2(0.5f, 1f);
            searchRect.anchoredPosition = new Vector2(0f, -40f);
            searchRect.offsetMin = new Vector2(14f, searchRect.offsetMin.y - 30f);
            searchRect.offsetMax = new Vector2(-14f, searchRect.offsetMax.y);
            searchRect.sizeDelta = new Vector2(searchRect.sizeDelta.x, 30f);
            var searchBackground = searchRect.gameObject.AddComponent<Image>();
            searchBackground.color = FieldColor;

            Text searchText = CreateText(searchRect, "Text", "", 15, TextAnchor.MiddleLeft, FontStyle.Normal);
            Stretch(searchText.rectTransform, 10f, 10f, 4f, 4f);
            searchText.supportRichText = false;

            Text placeholder = CreateText(searchRect, "Placeholder", "Search by name or ID…", 15, TextAnchor.MiddleLeft, FontStyle.Italic);
            Stretch(placeholder.rectTransform, 10f, 10f, 4f, 4f);
            placeholder.color = new Color(1f, 1f, 1f, 0.35f);

            var inputField = searchRect.gameObject.AddComponent<InputField>();
            inputField.targetGraphic = searchBackground;
            inputField.textComponent = searchText;
            inputField.placeholder = placeholder;

            // --- Tab row ---------------------------------------------------
            RectTransform tabRow = CreateRect("Tabs", panel);
            tabRow.anchorMin = new Vector2(0f, 1f);
            tabRow.anchorMax = new Vector2(1f, 1f);
            tabRow.pivot = new Vector2(0.5f, 1f);
            tabRow.anchoredPosition = new Vector2(0f, -78f);
            tabRow.offsetMin = new Vector2(14f, tabRow.offsetMin.y - 28f);
            tabRow.offsetMax = new Vector2(-14f, tabRow.offsetMax.y);
            tabRow.sizeDelta = new Vector2(tabRow.sizeDelta.x, 28f);
            var tabLayout = tabRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            tabLayout.spacing = 4f;
            tabLayout.childAlignment = TextAnchor.MiddleLeft;
            tabLayout.childControlWidth = false;
            tabLayout.childControlHeight = false;
            tabLayout.childForceExpandWidth = false;
            tabLayout.childForceExpandHeight = false;

            GameObject tabTemplate = BuildTabTemplate(tabRow);

            // --- Scrollable entry grid ------------------------------------
            RectTransform scrollArea = CreateRect("EntryScroll", panel);
            scrollArea.anchorMin = new Vector2(0f, 0f);
            scrollArea.anchorMax = new Vector2(1f, 1f);
            scrollArea.offsetMin = new Vector2(14f, 40f);
            scrollArea.offsetMax = new Vector2(-14f, -112f);
            var scrollRect = scrollArea.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.scrollSensitivity = 30f;

            RectTransform viewport = CreateRect("Viewport", scrollArea);
            Stretch(viewport, 0f, 0f, 0f, 0f);
            viewport.gameObject.AddComponent<RectMask2D>();
            // An (invisible) graphic makes the whole viewport a raycast surface for scroll-wheel input.
            var viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.01f);

            RectTransform content = CreateRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(EntrySize, EntrySize);
            grid.spacing = new Vector2(Spacing, Spacing);
            grid.padding = new RectOffset(4, 4, 4, 4);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 8;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = viewport;
            scrollRect.content = content;

            GameObject entryTemplate = BuildEntryTemplate(content);

            // --- Toast line ------------------------------------------------
            Text toast = CreateText(panel, "Toast", "", 15, TextAnchor.MiddleCenter, FontStyle.Bold);
            toast.rectTransform.anchorMin = new Vector2(0f, 0f);
            toast.rectTransform.anchorMax = new Vector2(1f, 0f);
            toast.rectTransform.pivot = new Vector2(0.5f, 0f);
            toast.rectTransform.anchoredPosition = new Vector2(0f, 8f);
            toast.rectTransform.sizeDelta = new Vector2(-28f, 26f);
            toast.color = new Color(1f, 0.92f, 0.6f, 1f);

            // --- Wire everything -------------------------------------------
            Wire(controller,
                ("playerReferences", playerReferences), ("inventory", inventory),
                ("creativeMode", creativeMode), ("inventoryUI", inventoryUI),
                ("panel", panel.gameObject), ("searchField", inputField),
                ("tabTemplate", tabTemplate), ("entryTemplate", entryTemplate),
                ("scrollRect", scrollRect), ("toastText", toast));

            EditorSceneManager.MarkSceneDirty(canvasObject.scene);
            Selection.activeGameObject = canvasObject;
            Debug.Log("Creative menu UI built and wired (F1 to open in play mode, gated by CreativeModeController on the player). Save the scene.");
        }

        // ------------------------------------------------------------------
        // Templates
        // ------------------------------------------------------------------

        private static GameObject BuildTabTemplate(Transform parent)
        {
            RectTransform root = CreateRect("TabTemplate", parent);
            root.sizeDelta = new Vector2(86f, 26f);
            var background = root.gameObject.AddComponent<Image>();
            background.color = CellColor;
            root.gameObject.AddComponent<Button>().targetGraphic = background;
            var view = root.gameObject.AddComponent<CreativeTabView>();

            Text label = CreateText(root, "Label", "Tab", 12, TextAnchor.MiddleCenter, FontStyle.Normal);
            Stretch(label.rectTransform, 2f, 2f, 2f, 2f);
            label.raycastTarget = false;

            Wire(view, ("background", background), ("label", label));

            root.gameObject.SetActive(false);
            return root.gameObject;
        }

        private static GameObject BuildEntryTemplate(Transform parent)
        {
            RectTransform root = CreateRect("EntryTemplate", parent);
            root.sizeDelta = new Vector2(EntrySize, EntrySize);
            var background = root.gameObject.AddComponent<Image>();
            background.color = CellColor;
            root.gameObject.AddComponent<Button>().targetGraphic = background;
            var view = root.gameObject.AddComponent<CreativeEntryView>();

            RectTransform icon = CreateRect("Icon", root);
            icon.anchorMin = new Vector2(0f, 0f);
            icon.anchorMax = new Vector2(1f, 1f);
            icon.offsetMin = new Vector2(10f, 26f);
            icon.offsetMax = new Vector2(-10f, -6f);
            var iconImage = icon.gameObject.AddComponent<Image>();
            iconImage.raycastTarget = false;
            iconImage.preserveAspect = true;

            Text label = CreateText(root, "Label", "", 11, TextAnchor.MiddleCenter, FontStyle.Normal);
            label.rectTransform.anchorMin = new Vector2(0f, 0f);
            label.rectTransform.anchorMax = new Vector2(1f, 0f);
            label.rectTransform.pivot = new Vector2(0.5f, 0f);
            label.rectTransform.anchoredPosition = new Vector2(0f, 3f);
            label.rectTransform.sizeDelta = new Vector2(-6f, 22f);
            label.raycastTarget = false;

            Wire(view, ("icon", iconImage), ("label", label));

            root.gameObject.SetActive(false);
            return root.gameObject;
        }

        // ------------------------------------------------------------------
        // Helpers (same conventions as InventoryUIBuilder)
        // ------------------------------------------------------------------

        private static T EnsureComponent<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            if (component == null)
            {
                component = Undo.AddComponent<T>(target);
                Debug.Log($"Added {typeof(T).Name} to '{target.name}'.", target);
            }

            return component;
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
                    Debug.LogError($"CreativeMenuUIBuilder: no serialized field '{field}' on {target.GetType().Name}.");
                    continue;
                }

                property.objectReferenceValue = value;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
