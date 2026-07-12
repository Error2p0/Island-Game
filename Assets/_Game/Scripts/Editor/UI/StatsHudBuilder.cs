using System.Collections.Generic;
using IslandGame.Data.Stats;
using IslandGame.EditorTools.Data;
using IslandGame.Player;
using IslandGame.Stats;
using IslandGame.Stats.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace IslandGame.EditorTools
{
    /// <summary>
    /// One-click stats HUD (same builder workflow as InventoryUIBuilder):
    /// builds a screen-space canvas with one slim bar per HUD-flagged
    /// StatDefinition (Health, Stamina, Hunger, Thirst, Warmth by default),
    /// stacked bottom-left in the minimal style of the reference games, and
    /// wires StatsHudView to the player's StatContainer. Bars are plain scene
    /// objects — restyle freely, the view only cares about wired references.
    /// Idempotent: refuses to build a second StatsHudCanvas.
    /// </summary>
    public static class StatsHudBuilder
    {
        private const string CanvasName = "StatsHudCanvas";
        private const float BarWidth = 190f;
        private const float BarHeight = 14f;
        private const float BarSpacing = 5f;

        private static readonly Color BackgroundColor = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color LabelColor = new Color(1f, 1f, 1f, 0.75f);

        [MenuItem("Island Game/UI/Build Stats HUD")]
        public static void Build()
        {
            if (GameObject.Find(CanvasName) != null)
            {
                Debug.LogWarning($"'{CanvasName}' already exists in the scene — delete it first to rebuild.");
                Selection.activeGameObject = GameObject.Find(CanvasName);
                return;
            }

            var player = Object.FindFirstObjectByType<PlayerReferences>();
            if (player == null)
            {
                Debug.LogError("No PlayerReferences found in the open scene — open the gameplay scene with the player first.");
                return;
            }

            var container = player.GetComponent<StatContainer>();
            if (container == null)
            {
                Debug.LogError("Player has no StatContainer — run Island Game/World/Add Stats System To Player first.");
                return;
            }

            var database = AssetDatabase.LoadAssetAtPath<StatDatabase>(
                $"{DefinitionDatabaseSync.DatabaseFolder}/StatDatabase.asset");
            if (database == null || database.Count == 0)
            {
                Debug.LogError("StatDatabase missing/empty — run Island Game/Data/Create Player Stat Definitions first.");
                return;
            }

            // --- Canvas root --------------------------------------------
            var canvasObject = new GameObject(CanvasName);
            Undo.RegisterCreatedObjectUndo(canvasObject, "Build Stats HUD");
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            var hudView = canvasObject.AddComponent<StatsHudView>();

            // --- Bar stack (bottom-left, above nothing — the hotbar is bottom-center) ---
            RectTransform stack = CreateRect("StatBars", canvasObject.transform);
            stack.anchorMin = stack.anchorMax = new Vector2(0f, 0f);
            stack.pivot = new Vector2(0f, 0f);
            stack.anchoredPosition = new Vector2(18f, 16f);

            var bindings = new List<(string statId, StatBarView bar)>();
            int barIndex = 0;
            IReadOnlyList<StatDefinition> all = database.All;
            for (int i = 0; i < all.Count; i++)
            {
                StatDefinition stat = all[i];
                if (stat == null || !stat.ShowOnHud)
                    continue;

                StatBarView bar = BuildBar(stack, stat, barIndex);
                bindings.Add((stat.Id, bar));
                barIndex++;
            }

            stack.sizeDelta = new Vector2(BarWidth, barIndex * (BarHeight + BarSpacing));

            // --- Wire the view -------------------------------------------
            var serialized = new SerializedObject(hudView);
            serialized.FindProperty("statContainer").objectReferenceValue = container;
            SerializedProperty list = serialized.FindProperty("bindings");
            list.arraySize = bindings.Count;
            for (int i = 0; i < bindings.Count; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("statId").stringValue = bindings[i].statId;
                element.FindPropertyRelative("bar").objectReferenceValue = bindings[i].bar;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(canvasObject.scene);
            Selection.activeGameObject = canvasObject;
            Debug.Log(
                $"Stats HUD built: {bindings.Count} bar(s) bottom-left, wired to the player's StatContainer " +
                "(event-driven, no polling). Save the scene.");
        }

        // ------------------------------------------------------------------
        // Pieces
        // ------------------------------------------------------------------

        private static StatBarView BuildBar(RectTransform stack, StatDefinition stat, int index)
        {
            // Stacked bottom-up: index 0 sits lowest.
            RectTransform root = CreateRect($"Bar_{stat.Id}", stack);
            root.anchorMin = root.anchorMax = new Vector2(0f, 0f);
            root.pivot = new Vector2(0f, 0f);
            root.anchoredPosition = new Vector2(0f, index * (BarHeight + BarSpacing));
            root.sizeDelta = new Vector2(BarWidth, BarHeight);

            var background = root.gameObject.AddComponent<Image>();
            background.color = BackgroundColor;
            background.raycastTarget = false;

            RectTransform fillRect = CreateRect("Fill", root);
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(1.5f, 1.5f);
            fillRect.offsetMax = new Vector2(-1.5f, -1.5f);
            var fill = fillRect.gameObject.AddComponent<Image>();
            // A filled Image needs SOME sprite to fill; Unity's built-in white works.
            fill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 1f;
            fill.color = stat.BarColor;
            fill.raycastTarget = false;

            RectTransform labelRect = CreateRect("Label", root);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(6f, 0f);
            labelRect.offsetMax = new Vector2(-4f, 0f);
            var label = labelRect.gameObject.AddComponent<Text>();
            label.text = stat.DisplayName.ToUpperInvariant();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 9;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleLeft;
            label.color = LabelColor;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;

            var view = root.gameObject.AddComponent<StatBarView>();
            var serialized = new SerializedObject(view);
            serialized.FindProperty("fillImage").objectReferenceValue = fill;
            serialized.FindProperty("label").objectReferenceValue = label;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return view;
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
