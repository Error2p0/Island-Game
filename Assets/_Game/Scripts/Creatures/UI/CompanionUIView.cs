using IslandGame.Inventory.UI;
using IslandGame.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace IslandGame.Creatures.UI
{
    /// <summary>
    /// The companion system's two small UI surfaces on one canvas:
    ///
    ///   NAMING PANEL — opens when a creature is tamed (basic text input:
    ///     field + confirm, Enter works too). Takes the shared refcounted
    ///     UIInputFocus like every other panel, so gameplay input blocks and
    ///     the cursor frees without this panel knowing about any other.
    ///
    ///   MESSAGE LINE — a top-center toast for companion moments ("Rex will
    ///     now stay here.", "Rex has died.") — the name-tagged feedback that
    ///     makes a companion's death read differently from generic wildlife.
    ///
    /// Static Instance (the WeatherController pattern) so CreatureTaming can
    /// reach it without wiring; a scene without the canvas degrades to
    /// console logs. Built by Island Game/UI/Build Companion UI.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionUIView : MonoBehaviour
    {
        [Header("Wired by the builder")]
        [SerializeField] private GameObject namePanel;
        [SerializeField] private InputField nameInput;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Text messageText;

        /// <summary>The scene's companion UI; null when it was never built.</summary>
        public static CompanionUIView Instance { get; private set; }

        private CreatureTaming pendingNaming;
        private PlayerReferences player;
        private float messageUntil;

        private void OnEnable()
        {
            Instance = this;
            if (confirmButton != null)
                confirmButton.onClick.AddListener(ConfirmName);
        }

        private void OnDisable()
        {
            if (Instance == this)
                Instance = null;
            if (confirmButton != null)
                confirmButton.onClick.RemoveListener(ConfirmName);
        }

        // ------------------------------------------------------------------
        // Naming
        // ------------------------------------------------------------------

        /// <summary>Opens the naming panel for a freshly tamed companion.</summary>
        public void PromptForName(CreatureTaming companion)
        {
            if (companion == null || namePanel == null)
            {
                companion?.SetName(null); // no panel: keep the species name
                return;
            }

            // A second tame while naming: settle the first with its default.
            if (pendingNaming != null)
                pendingNaming.SetName(nameInput != null ? nameInput.text : null);
            else
                UIInputFocus.Acquire(ResolveInput());

            pendingNaming = companion;
            if (nameInput != null)
            {
                nameInput.text = companion.CompanionName;
                nameInput.Select();
                nameInput.ActivateInputField();
            }

            namePanel.SetActive(true);
        }

        private void ConfirmName()
        {
            if (pendingNaming == null)
                return;

            CreatureTaming companion = pendingNaming;
            pendingNaming = null;

            namePanel.SetActive(false);
            UIInputFocus.Release(ResolveInput());

            companion.SetName(nameInput != null ? nameInput.text : null);
        }

        // ------------------------------------------------------------------
        // Messages
        // ------------------------------------------------------------------

        /// <summary>Top-center toast; later calls replace earlier ones.</summary>
        public void ShowMessage(string text, float seconds)
        {
            if (messageText == null)
            {
                Debug.Log($"[Companion] {text}");
                return;
            }

            messageText.text = text;
            messageText.gameObject.SetActive(true);
            messageUntil = Time.time + seconds;
        }

        private void Update()
        {
            if (namePanel != null && namePanel.activeSelf)
            {
                UIInputFocus.EnforceCursor();

                if (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame)
                    ConfirmName();
            }

            if (messageText != null && messageText.gameObject.activeSelf && Time.time >= messageUntil)
                messageText.gameObject.SetActive(false);
        }

        private PlayerInputHandler ResolveInput()
        {
            if (player == null)
                player = FindFirstObjectByType<PlayerReferences>();

            return player != null ? player.InputHandler : null;
        }
    }
}
