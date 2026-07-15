using IslandGame.EditorTools.Data;
using IslandGame.Player;
using IslandGame.Player.UI;
using IslandGame.Stats;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace IslandGame.EditorTools
{
    /// <summary>
    /// One-click respawn/death setup: PlayerRespawnController on the player
    /// (wired to the default DropBackpackDeathPenalty asset — created on
    /// demand) plus the DeathScreenCanvas — fade, "YOU DIED", cause line,
    /// countdown Respawn button, and the death marker HUD element. Plain
    /// scene objects in the established builder style; restyle freely, the
    /// views only care about wired references. Idempotent: refuses to build
    /// a second DeathScreenCanvas; the controller is added only when missing.
    /// </summary>
    public static class RespawnSystemBuilder
    {
        private const string CanvasName = "DeathScreenCanvas";

        [MenuItem("Island Game/World/Add Respawn System")]
        public static void Create()
        {
            var player = Object.FindFirstObjectByType<PlayerReferences>();
            if (player == null)
            {
                Debug.LogError("Respawn System: no player in the scene — run the player builders first.");
                return;
            }

            if (player.GetComponent<StatContainer>() == null || player.GetComponent<PlayerHealth>() == null)
            {
                Debug.LogError("Respawn System: player is missing StatContainer/PlayerHealth — run Island Game/World/Add Stats System To Player first.");
                return;
            }

            // --- UI canvas ---------------------------------------------------
            DeathScreenView screenView;
            DeathMarkerView markerView;
            GameObject existingCanvas = GameObject.Find(CanvasName);
            if (existingCanvas != null)
            {
                screenView = existingCanvas.GetComponent<DeathScreenView>();
                markerView = existingCanvas.GetComponent<DeathMarkerView>();
                Debug.Log($"Respawn System: reusing existing '{CanvasName}'.");
            }
            else
            {
                BuildCanvas(out screenView, out markerView);
            }

            // --- Controller on the player ------------------------------------
            var controller = player.GetComponent<PlayerRespawnController>();
            if (controller == null)
                controller = Undo.AddComponent<PlayerRespawnController>(player.gameObject);

            var serialized = new SerializedObject(controller);
            if (serialized.FindProperty("penaltyPolicy").objectReferenceValue == null)
                serialized.FindProperty("penaltyPolicy").objectReferenceValue = DeathContentCreator.LoadOrCreatePolicy();
            serialized.FindProperty("deathScreen").objectReferenceValue = screenView;
            serialized.FindProperty("deathMarker").objectReferenceValue = markerView;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
            Selection.activeGameObject = player.gameObject;
            Debug.Log(
                "Respawn System ready: PlayerRespawnController (drop-backpack penalty, bed respawn points) + " +
                "DeathScreenCanvas. Save the scene.");
        }

        // ------------------------------------------------------------------
        // Canvas construction
        // ------------------------------------------------------------------

        private static void BuildCanvas(out DeathScreenView screenView, out DeathMarkerView markerView)
        {
            var canvasObject = new GameObject(CanvasName);
            Undo.RegisterCreatedObjectUndo(canvasObject, "Add Respawn System");
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50; // death screen sits above the other HUDs
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasObject.AddComponent<GraphicRaycaster>();

            screenView = canvasObject.AddComponent<DeathScreenView>();
            markerView = canvasObject.AddComponent<DeathMarkerView>();

            // --- Death panel (inactive until death) ------------------------
            RectTransform panel = CreateRect("DeathPanel", canvasObject.transform);
            Stretch(panel);

            var fade = panel.gameObject.AddComponent<Image>();
            fade.color = new Color(0f, 0f, 0f, 0f);
            fade.raycastTarget = true; // swallows clicks under the screen

            RectTransform titleRect = CreateRect("Title", panel);
            titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 0.62f);
            titleRect.sizeDelta = new Vector2(800f, 90f);
            Text title = MakeText(titleRect, "YOU DIED", 64, new Color(0.85f, 0.2f, 0.2f), FontStyle.Bold);

            RectTransform causeRect = CreateRect("Cause", panel);
            causeRect.anchorMin = causeRect.anchorMax = new Vector2(0.5f, 0.52f);
            causeRect.sizeDelta = new Vector2(800f, 40f);
            Text cause = MakeText(causeRect, "You died.", 22, new Color(0.85f, 0.82f, 0.8f), FontStyle.Normal);

            RectTransform buttonRect = CreateRect("RespawnButton", panel);
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0.38f);
            buttonRect.sizeDelta = new Vector2(280f, 52f);
            var buttonImage = buttonRect.gameObject.AddComponent<Image>();
            buttonImage.color = new Color(0.16f, 0.16f, 0.18f, 0.92f);
            var button = buttonRect.gameObject.AddComponent<Button>();
            button.targetGraphic = buttonImage;

            RectTransform buttonLabelRect = CreateRect("Label", buttonRect);
            Stretch(buttonLabelRect);
            Text buttonLabel = MakeText(buttonLabelRect, "RESPAWN", 22, Color.white, FontStyle.Bold);

            panel.gameObject.SetActive(false);

            // --- Death marker (screen-space icon + distance, hidden) --------
            RectTransform marker = CreateRect("DeathMarker", canvasObject.transform);
            marker.sizeDelta = new Vector2(160f, 44f);

            RectTransform iconRect = CreateRect("Icon", marker);
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 1f);
            iconRect.pivot = new Vector2(0.5f, 1f);
            iconRect.sizeDelta = new Vector2(18f, 18f);
            var icon = iconRect.gameObject.AddComponent<Image>();
            icon.color = new Color(0.9f, 0.75f, 0.3f, 0.95f);
            icon.raycastTarget = false;

            RectTransform distanceRect = CreateRect("Distance", marker);
            distanceRect.anchorMin = new Vector2(0f, 0f);
            distanceRect.anchorMax = new Vector2(1f, 1f);
            distanceRect.offsetMin = new Vector2(0f, 0f);
            distanceRect.offsetMax = new Vector2(0f, -18f);
            Text distance = MakeText(distanceRect, "Your remains · 0 m", 14, new Color(0.95f, 0.9f, 0.75f), FontStyle.Normal);

            marker.gameObject.SetActive(false);

            // --- Wire the views ---------------------------------------------
            var serializedScreen = new SerializedObject(screenView);
            serializedScreen.FindProperty("panelRoot").objectReferenceValue = panel.gameObject;
            serializedScreen.FindProperty("fadeImage").objectReferenceValue = fade;
            serializedScreen.FindProperty("causeText").objectReferenceValue = cause;
            serializedScreen.FindProperty("respawnButton").objectReferenceValue = button;
            serializedScreen.FindProperty("respawnButtonLabel").objectReferenceValue = buttonLabel;
            serializedScreen.ApplyModifiedPropertiesWithoutUndo();

            var serializedMarker = new SerializedObject(markerView);
            serializedMarker.FindProperty("markerRoot").objectReferenceValue = marker;
            serializedMarker.FindProperty("distanceText").objectReferenceValue = distance;
            serializedMarker.ApplyModifiedPropertiesWithoutUndo();

            _ = title; // built for looks; no view reference needed
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)gameObject.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
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
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
    }
}
