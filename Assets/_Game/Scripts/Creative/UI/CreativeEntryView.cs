using UnityEngine;
using UnityEngine.UI;

namespace IslandGame.Creative.UI
{
    /// <summary>
    /// One entry cell in the creative grid: icon + name, click to give.
    /// Pooled and rebound by CreativeMenuController on every filter change.
    /// Non-giveable entries (blocks without an item form) render dimmed but
    /// stay visible — clicking them explains itself via the toast.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CreativeEntryView : MonoBehaviour
    {
        [Header("Wired by the UI builder (on the template)")]
        [SerializeField] private Image icon;
        [SerializeField] private Text label;

        private CreativeMenuController controller;
        private CreativeEntry entry;

        private void Awake()
        {
            GetComponent<Button>().onClick.AddListener(OnClicked);
        }

        public void Bind(CreativeMenuController owner, CreativeEntry newEntry)
        {
            controller = owner;
            entry = newEntry;

            label.text = entry.DisplayName;

            bool giveable = entry.GiveItem != null;
            if (entry.Icon != null)
            {
                icon.enabled = true;
                icon.sprite = entry.Icon;
                icon.color = giveable ? Color.white : new Color(1f, 1f, 1f, 0.35f);
            }
            else
            {
                // Icon-less entries still show a visible, clickable square.
                icon.enabled = true;
                icon.sprite = null;
                icon.color = giveable ? new Color(1f, 1f, 1f, 0.25f) : new Color(1f, 0.4f, 0.4f, 0.2f);
            }

            label.color = giveable ? Color.white : new Color(1f, 1f, 1f, 0.45f);
        }

        private void OnClicked()
        {
            if (controller != null && entry != null)
                controller.GiveEntry(entry);
        }
    }
}
