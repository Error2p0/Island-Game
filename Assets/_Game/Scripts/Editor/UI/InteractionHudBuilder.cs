using IslandGame.Building;
using IslandGame.Interaction.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace IslandGame.EditorTools
{
    /// <summary>
    /// One-click interaction prompt HUD (same builder workflow as
    /// StatsHudBuilder): a screen-space canvas with one small "[E] ..." label
    /// centered under the crosshair, wired to the player's PlayerInteraction.
    /// Serves every IInteractable — foliage harvesting, campfires, doors,
    /// chests. Plain scene objects — restyle freely, the view only cares
    /// about wired references. Idempotent: refuses to build a second
    /// InteractionPromptCanvas.
    /// </summary>
    public static class InteractionHudBuilder
    {
        private const string CanvasName = "InteractionPromptCanvas";

        private static readonly Color BackgroundColor = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color LabelColor = new Color(1f, 1f, 1f, 0.9f);

        [MenuItem("Island Game/UI/Build Interaction Prompt HUD")]
        public static void Build()
        {
            if (GameObject.Find(CanvasName) != null)
            {
                Debug.LogWarning($"'{CanvasName}' already exists in the scene — delete it first to rebuild.");
                Selection.activeGameObject = GameObject.Find(CanvasName);
                return;
            }

            var interaction = Object.FindFirstObjectByType<PlayerInteraction>();
            if (interaction == null)
            {
                Debug.LogError(
                    "No PlayerInteraction found in the open scene — run Island Game/World/Add Building System To Player first.");
                return;
            }

            // --- Canvas root --------------------------------------------
            var canvasObject = new GameObject(CanvasName);
            Undo.RegisterCreatedObjectUndo(canvasObject, "Build Interaction Prompt HUD");
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            var view = canvasObject.AddComponent<InteractionPromptView>();

            // --- Prompt panel (center, below the crosshair) ---------------
            RectTransform panel = CreateRect("PromptPanel", canvasObject.transform);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 1f);
            panel.anchoredPosition = new Vector2(0f, -60f);
            panel.sizeDelta = new Vector2(320f, 26f);

            var background = panel.gameObject.AddComponent<Image>();
            background.color = BackgroundColor;
            background.raycastTarget = false;

            RectTransform labelRect = CreateRect("Label", panel);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8f, 0f);
            labelRect.offsetMax = new Vector2(-8f, 0f);
            var label = labelRect.gameObject.AddComponent<Text>();
            label.text = "[E] Interact";
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 15;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = LabelColor;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;

            // --- Wire the view -------------------------------------------
            var serialized = new SerializedObject(view);
            serialized.FindProperty("playerInteraction").objectReferenceValue = interaction;
            serialized.FindProperty("panel").objectReferenceValue = panel.gameObject;
            serialized.FindProperty("label").objectReferenceValue = label;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            panel.gameObject.SetActive(false); // hidden until something is aimed

            EditorSceneManager.MarkSceneDirty(canvasObject.scene);
            Selection.activeGameObject = canvasObject;
            Debug.Log(
                "Interaction prompt HUD built: one \"[E] ...\" label under the crosshair, wired to " +
                "PlayerInteraction.AimedPrompt — every IInteractable (foliage, campfire, door, chest) now shows " +
                "its hint. Save the scene.");
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)gameObject.transform;
            rect.SetParent(parent, false);
            return rect;
        }
    }
}
