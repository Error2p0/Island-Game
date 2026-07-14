using IslandGame.Building;
using UnityEngine;
using UnityEngine.UI;

namespace IslandGame.Interaction.UI
{
    /// <summary>
    /// Renders PlayerInteraction.AimedPrompt — the HUD half that interface
    /// reserved from day one ("a future HUD phase can render the hint
    /// without touching this logic"; this is that phase). A small "[E] ..."
    /// line under the crosshair that appears while an interactable with an
    /// active prompt is aimed: harvestable bushes, campfires, doors, chests —
    /// every IInteractable, one label.
    ///
    /// Polls the string each frame (it IS the contract — PlayerInteraction
    /// refreshes it per frame) and only touches the UI when it changes, so
    /// steady aim costs a string compare.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteractionPromptView : MonoBehaviour
    {
        [Tooltip("The player's interaction component. Wired by the builder; auto-resolved when empty.")]
        [SerializeField] private PlayerInteraction playerInteraction;

        [Tooltip("Panel shown while a prompt is active (the label's background).")]
        [SerializeField] private GameObject panel;

        [Tooltip("Label receiving the \"[E] Harvest Berry Bush\" text.")]
        [SerializeField] private Text label;

        private string currentPrompt;

        private void Start()
        {
            if (playerInteraction == null)
                playerInteraction = FindFirstObjectByType<PlayerInteraction>();

            Apply(null);
        }

        private void Update()
        {
            string prompt = playerInteraction != null ? playerInteraction.AimedPrompt : null;
            if (prompt == currentPrompt)
                return;

            Apply(prompt);
        }

        private void Apply(string prompt)
        {
            currentPrompt = prompt;

            bool show = !string.IsNullOrEmpty(prompt);
            if (panel != null && panel.activeSelf != show)
                panel.SetActive(show);

            if (show && label != null)
                label.text = $"[E] {prompt}";
        }
    }
}
