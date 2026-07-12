using UnityEngine;
using UnityEngine.UI;

namespace IslandGame.Stats.UI
{
    /// <summary>
    /// One HUD stat bar: a filled Image plus a small label. Pure view — it
    /// knows nothing about stats; StatsHudView pushes normalized values into
    /// it when the container's OnStatChanged fires. Below the low threshold
    /// the fill dims toward a warning tint so a draining vital reads at a
    /// glance without any extra UI.
    /// </summary>
    public sealed class StatBarView : MonoBehaviour
    {
        [Tooltip("Wired by the HUD builder: the fill image (Image Type = Filled, Horizontal).")]
        [SerializeField] private Image fillImage;

        [Tooltip("Wired by the HUD builder: the small stat label.")]
        [SerializeField] private Text label;

        [Tooltip("Below this normalized value the fill blends toward the warning color.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float lowThreshold = 0.25f;

        [SerializeField] private Color warningColor = new Color(0.9f, 0.15f, 0.1f);

        private Color baseColor = Color.white;

        private void Awake()
        {
            // The HUD builder authors the color straight into the fill Image;
            // adopt it as the base so the low-warning blend returns to it.
            if (fillImage != null)
                baseColor = fillImage.color;
        }

        /// <summary>Overrides the bar's identity at runtime (rebinding without the builder).</summary>
        public void Configure(string labelText, Color color)
        {
            baseColor = color;
            if (label != null)
                label.text = labelText;
            if (fillImage != null)
                fillImage.color = color;
        }

        /// <summary>Updates the fill; called only when the stat actually changed.</summary>
        public void SetNormalized(float normalized)
        {
            if (fillImage == null)
                return;

            normalized = Mathf.Clamp01(normalized);
            fillImage.fillAmount = normalized;

            fillImage.color = normalized < lowThreshold && lowThreshold > 0f
                ? Color.Lerp(warningColor, baseColor, normalized / lowThreshold)
                : baseColor;
        }
    }
}
