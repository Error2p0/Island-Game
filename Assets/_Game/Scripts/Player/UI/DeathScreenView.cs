using System;
using UnityEngine;
using UnityEngine.UI;

namespace IslandGame.Player.UI
{
    /// <summary>
    /// The death screen: darkening fade, "YOU DIED", cause-of-death line and
    /// a Respawn button that unlocks after the controller's delay (its label
    /// counts down until then). Pure view — PlayerRespawnController owns the
    /// flow and input focus; this only renders state it is handed and
    /// forwards the button press. Built by the Respawn System builder as
    /// plain scene objects (restyle freely, the view only cares about wired
    /// references).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeathScreenView : MonoBehaviour
    {
        [Header("Wired by the builder")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Image fadeImage;
        [SerializeField] private Text causeText;
        [SerializeField] private Button respawnButton;
        [SerializeField] private Text respawnButtonLabel;

        [Header("Fade")]
        [Tooltip("Final darkness of the backdrop.")]
        [Range(0f, 1f)]
        [SerializeField] private float fadeTargetAlpha = 0.82f;

        [Tooltip("Seconds to reach full darkness.")]
        [Range(0.1f, 5f)]
        [SerializeField] private float fadeSeconds = 1.2f;

        private Action onRespawnPressed;
        private float shownAtTime;

        /// <summary>Opens the screen with the cause line; the callback fires when the (unlocked) button is pressed.</summary>
        public void Show(string cause, Action respawnCallback)
        {
            onRespawnPressed = respawnCallback;
            shownAtTime = Time.time;

            if (causeText != null)
                causeText.text = cause;

            if (fadeImage != null)
            {
                Color color = fadeImage.color;
                color.a = 0f;
                fadeImage.color = color;
            }

            if (panelRoot != null)
                panelRoot.SetActive(true);
        }

        /// <summary>Controller-driven: locks/unlocks the button and renders the countdown on its label.</summary>
        public void SetRespawnAvailable(bool available, float secondsRemaining)
        {
            if (respawnButton != null)
                respawnButton.interactable = available;

            if (respawnButtonLabel != null)
            {
                respawnButtonLabel.text = available
                    ? "RESPAWN  (Space)"
                    : $"RESPAWN IN {Mathf.CeilToInt(Mathf.Max(0f, secondsRemaining))}…";
            }
        }

        public void Hide()
        {
            onRespawnPressed = null;
            if (panelRoot != null)
                panelRoot.SetActive(false);
        }

        private void OnEnable()
        {
            if (respawnButton != null)
                respawnButton.onClick.AddListener(HandleButton);
        }

        private void OnDisable()
        {
            if (respawnButton != null)
                respawnButton.onClick.RemoveListener(HandleButton);
        }

        private void Update()
        {
            if (panelRoot == null || !panelRoot.activeSelf || fadeImage == null)
                return;

            float t = Mathf.Clamp01((Time.time - shownAtTime) / fadeSeconds);
            Color color = fadeImage.color;
            color.a = fadeTargetAlpha * t;
            fadeImage.color = color;
        }

        private void HandleButton()
        {
            onRespawnPressed?.Invoke();
        }
    }
}
