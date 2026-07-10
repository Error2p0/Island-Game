using UnityEngine;
using UnityEngine.UI;

namespace IslandGame.Crafting.UI
{
    /// <summary>
    /// One ingredient line in the crafting detail panel: item name plus a
    /// have/need counter colored green when the inventory covers it and red
    /// when it doesn't. Pooled and rebound on every refresh.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class IngredientRowView : MonoBehaviour
    {
        private static readonly Color EnoughColor = new Color(0.45f, 0.95f, 0.45f);
        private static readonly Color MissingColor = new Color(1f, 0.4f, 0.35f);

        [Header("Wired by the UI builder (on the template)")]
        [SerializeField] private Text nameLabel;
        [SerializeField] private Text countLabel;

        public void Bind(string itemName, int have, int need)
        {
            nameLabel.text = itemName;
            countLabel.text = $"{have}/{need}";
            countLabel.color = have >= need ? EnoughColor : MissingColor;
        }
    }
}
