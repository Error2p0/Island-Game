using UnityEngine;
using UnityEngine.UI;

namespace IslandGame.Creative.UI
{
    /// <summary>
    /// One category tab button in the creative menu, instantiated from the
    /// builder's template. Selection tinting only — which tab is active lives
    /// on the CreativeMenuController.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CreativeTabView : MonoBehaviour
    {
        private static readonly Color NormalColor = new Color(0.12f, 0.12f, 0.14f, 0.9f);
        private static readonly Color SelectedColor = new Color(0.32f, 0.3f, 0.16f, 1f);

        [Header("Wired by the UI builder (on the template)")]
        [SerializeField] private Image background;
        [SerializeField] private Text label;

        public void Bind(CreativeMenuController controller, int tabIndex, string tabLabel)
        {
            label.text = tabLabel;

            var button = GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => controller.SelectTab(tabIndex));
        }

        public void SetSelected(bool selected)
        {
            background.color = selected ? SelectedColor : NormalColor;
        }
    }
}
