using IslandGame.Creatures.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace IslandGame.EditorTools
{
    /// <summary>
    /// One-click companion UI (same builder workflow as the other HUD
    /// builders): a canvas with the naming panel (label + input field +
    /// confirm button, hidden until a tame) and the top-center companion
    /// message line ("Rex will now stay here.", "Rex has died."). Plain scene
    /// objects — restyle freely, the view only cares about wired references.
    /// Idempotent: refuses to build a second CompanionUICanvas.
    /// </summary>
    public static class CompanionUIBuilder
    {
        private const string CanvasName = "CompanionUICanvas";

        private static readonly Color PanelColor = new Color(0.08f, 0.08f, 0.1f, 0.92f);
        private static readonly Color FieldColor = new Color(0.16f, 0.16f, 0.19f, 1f);

        [MenuItem("Island Game/UI/Build Companion UI")]
        public static void Build()
        {
            if (GameObject.Find(CanvasName) != null)
            {
                Debug.LogWarning($"'{CanvasName}' already exists in the scene — delete it first to rebuild.");
                Selection.activeGameObject = GameObject.Find(CanvasName);
                return;
            }

            var canvasObject = new GameObject(CanvasName);
            Undo.RegisterCreatedObjectUndo(canvasObject, "Build Companion UI");
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40; // above HUD, below the death screen (50)
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasObject.AddComponent<GraphicRaycaster>();

            var view = canvasObject.AddComponent<CompanionUIView>();

            // --- Naming panel (hidden until a tame) -------------------------
            RectTransform panel = CreateRect("NamePanel", canvasObject.transform);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(380f, 150f);
            var panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.color = PanelColor;

            RectTransform titleRect = CreateRect("Title", panel);
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -10f);
            titleRect.sizeDelta = new Vector2(0f, 28f);
            MakeText(titleRect, "Name your companion", 18, new Color(0.95f, 0.92f, 0.85f), FontStyle.Bold);

            RectTransform fieldRect = CreateRect("NameField", panel);
            fieldRect.anchorMin = fieldRect.anchorMax = new Vector2(0.5f, 0.5f);
            fieldRect.anchoredPosition = new Vector2(0f, 4f);
            fieldRect.sizeDelta = new Vector2(320f, 34f);
            var fieldImage = fieldRect.gameObject.AddComponent<Image>();
            fieldImage.color = FieldColor;
            var input = fieldRect.gameObject.AddComponent<InputField>();

            RectTransform inputTextRect = CreateRect("Text", fieldRect);
            inputTextRect.anchorMin = Vector2.zero;
            inputTextRect.anchorMax = Vector2.one;
            inputTextRect.offsetMin = new Vector2(10f, 4f);
            inputTextRect.offsetMax = new Vector2(-10f, -4f);
            Text inputText = MakeText(inputTextRect, string.Empty, 18, Color.white, FontStyle.Normal);
            inputText.alignment = TextAnchor.MiddleLeft;
            inputText.supportRichText = false;

            input.textComponent = inputText;
            input.targetGraphic = fieldImage;
            input.characterLimit = 24;

            RectTransform buttonRect = CreateRect("ConfirmButton", panel);
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0f);
            buttonRect.pivot = new Vector2(0.5f, 0f);
            buttonRect.anchoredPosition = new Vector2(0f, 12f);
            buttonRect.sizeDelta = new Vector2(160f, 34f);
            var buttonImage = buttonRect.gameObject.AddComponent<Image>();
            buttonImage.color = new Color(0.22f, 0.36f, 0.22f, 1f);
            var button = buttonRect.gameObject.AddComponent<Button>();
            button.targetGraphic = buttonImage;

            RectTransform buttonLabelRect = CreateRect("Label", buttonRect);
            buttonLabelRect.anchorMin = Vector2.zero;
            buttonLabelRect.anchorMax = Vector2.one;
            buttonLabelRect.offsetMin = Vector2.zero;
            buttonLabelRect.offsetMax = Vector2.zero;
            MakeText(buttonLabelRect, "CONFIRM  (Enter)", 15, Color.white, FontStyle.Bold);

            panel.gameObject.SetActive(false);

            // --- Message line (top-center toast, hidden) --------------------
            RectTransform messageRect = CreateRect("CompanionMessage", canvasObject.transform);
            messageRect.anchorMin = messageRect.anchorMax = new Vector2(0.5f, 1f);
            messageRect.pivot = new Vector2(0.5f, 1f);
            messageRect.anchoredPosition = new Vector2(0f, -60f);
            messageRect.sizeDelta = new Vector2(700f, 32f);
            Text message = MakeText(messageRect, string.Empty, 20, new Color(0.95f, 0.9f, 0.75f), FontStyle.Bold);
            messageRect.gameObject.SetActive(false);

            // --- Wire the view ---------------------------------------------
            var serialized = new SerializedObject(view);
            serialized.FindProperty("namePanel").objectReferenceValue = panel.gameObject;
            serialized.FindProperty("nameInput").objectReferenceValue = input;
            serialized.FindProperty("confirmButton").objectReferenceValue = button;
            serialized.FindProperty("messageText").objectReferenceValue = message;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(canvasObject.scene);
            Selection.activeGameObject = canvasObject;
            Debug.Log("Companion UI built: naming panel + companion message line. Save the scene.");
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)gameObject.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static Text MakeText(RectTransform rect, string content, int size, Color color, FontStyle style)
        {
            var text = rect.gameObject.AddComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            return text;
        }
    }
}
