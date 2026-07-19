using UnityEngine;
using UnityEngine.UI;

namespace IslandGame.Building.UI
{
    /// <summary>
    /// The hold-to-deconstruct progress bar: a slim horizontal fill under the
    /// crosshair (just below the "[E] ..." interaction prompt line) with the
    /// targeted piece's name, driven by BuildingDeconstructor every held
    /// frame. Auto-created on first use — the MiningDebrisEffect pattern, no
    /// scene setup and no builder step, so the timed deconstruct works in any
    /// scene the moment the code does. The fill is a plain anchored rect
    /// (anchorMax.x = progress) rather than a Filled Image, because the
    /// builtin fill sprite the HUD builders use is editor-only.
    /// </summary>
    public sealed class DeconstructProgressView : MonoBehaviour
    {
        private static DeconstructProgressView instance;

        private static readonly Color BackgroundColor = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color TrackColor = new Color(1f, 1f, 1f, 0.12f);
        private static readonly Color FillColor = new Color(1f, 0.62f, 0.18f, 0.95f);
        private static readonly Color LabelColor = new Color(1f, 1f, 1f, 0.9f);

        private GameObject panel;
        private RectTransform fillRect;
        private Text label;
        private string currentName;

        /// <summary>Scene singleton, created on demand.</summary>
        public static DeconstructProgressView GetOrCreate()
        {
            if (instance == null)
            {
                instance = FindFirstObjectByType<DeconstructProgressView>();
                if (instance == null)
                    instance = new GameObject("DeconstructProgress").AddComponent<DeconstructProgressView>();
            }

            return instance;
        }

        /// <summary>Hides the bar without ever creating the canvas — safe to call every idle frame.</summary>
        public static void HideIfAlive()
        {
            if (instance != null)
                instance.Hide();
        }

        public void Show(string pieceName, float progress01)
        {
            EnsureUI();

            if (!panel.activeSelf)
                panel.SetActive(true);

            if (pieceName != currentName)
            {
                currentName = pieceName;
                label.text = $"Deconstructing {pieceName}";
            }

            Vector2 anchorMax = fillRect.anchorMax;
            anchorMax.x = Mathf.Clamp01(progress01);
            fillRect.anchorMax = anchorMax;
        }

        public void Hide()
        {
            if (panel != null && panel.activeSelf)
                panel.SetActive(false);
        }

        private void EnsureUI()
        {
            if (panel != null)
                return;

            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            // Below the interaction prompt panel (its bottom edge sits at -86),
            // so "[E] Open" on a door and the deconstruct bar can stack.
            RectTransform panelRect = CreateRect("Panel", transform);
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 1f);
            panelRect.anchoredPosition = new Vector2(0f, -92f);
            panelRect.sizeDelta = new Vector2(260f, 34f);
            panel = panelRect.gameObject;

            var background = panel.AddComponent<Image>();
            background.color = BackgroundColor;
            background.raycastTarget = false;

            RectTransform labelRect = CreateRect("Label", panelRect);
            labelRect.anchorMin = new Vector2(0f, 0.45f);
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8f, 0f);
            labelRect.offsetMax = new Vector2(-8f, 0f);
            label = labelRect.gameObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 13;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = LabelColor;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;

            RectTransform trackRect = CreateRect("Track", panelRect);
            trackRect.anchorMin = Vector2.zero;
            trackRect.anchorMax = new Vector2(1f, 0.45f);
            trackRect.offsetMin = new Vector2(8f, 6f);
            trackRect.offsetMax = new Vector2(-8f, -2f);
            var track = trackRect.gameObject.AddComponent<Image>();
            track.color = TrackColor;
            track.raycastTarget = false;

            fillRect = CreateRect("Fill", trackRect);
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.offsetMin = Vector2.one;
            fillRect.offsetMax = -Vector2.one;
            var fill = fillRect.gameObject.AddComponent<Image>();
            fill.color = FillColor;
            fill.raycastTarget = false;

            panel.SetActive(false);
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var rectObject = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)rectObject.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }
    }
}
