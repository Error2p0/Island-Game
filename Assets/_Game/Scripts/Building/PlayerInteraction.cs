using IslandGame.Interaction;
using IslandGame.Player;
using UnityEngine;

namespace IslandGame.Building
{
    /// <summary>
    /// The player half of the interact contract from Building Phase 1:
    /// E (or an "Interact" input action) raycasts the shared camera aim ray
    /// and calls Interact on the IInteractable being looked at. Originally
    /// only functional placeables (campfire fueling/lighting, workbench menu,
    /// storage, doors); the foliage phase generalized the lookup to
    /// IInteractable so world objects (harvestable bushes) ride the exact
    /// same input path — the logic here did not change.
    ///
    /// AimedPrompt is refreshed every frame so the HUD (InteractionPromptView
    /// since the foliage phase) can render the "[E] Light campfire" hint
    /// without touching this logic — null while nothing interactable is
    /// aimed, or while the aimed object reports null (nothing to do).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerReferences))]
    public sealed class PlayerInteraction : MonoBehaviour
    {
        [Tooltip("Maximum interaction distance from the camera, meters.")]
        [SerializeField] private float reach = 3.5f;

        private PlayerReferences references;
        private readonly RaycastHit[] rayBuffer = new RaycastHit[16];

        /// <summary>Prompt of the aimed interactable, null when none (or none active). Rendered by InteractionPromptView.</summary>
        public string AimedPrompt { get; private set; }

        private void Awake()
        {
            references = GetComponent<PlayerReferences>();
        }

        private void OnEnable()
        {
            references.InputHandler.InteractPressed += TryInteract;
        }

        private void OnDisable()
        {
            references.InputHandler.InteractPressed -= TryInteract;
            AimedPrompt = null;
        }

        private void Update()
        {
            IInteractable interactable = FindAimedInteractable();
            AimedPrompt = interactable?.InteractionPrompt;
        }

        private void TryInteract()
        {
            IInteractable interactable = FindAimedInteractable();
            interactable?.Interact(gameObject);
        }

        private IInteractable FindAimedInteractable()
        {
            if (!PlayerAimRaycast.Raycast(references.CameraPivot, transform, reach, rayBuffer, out RaycastHit hit))
                return null;

            return hit.collider.GetComponentInParent<IInteractable>();
        }
    }
}
