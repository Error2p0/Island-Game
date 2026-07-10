using IslandGame.Player;
using UnityEngine;

namespace IslandGame.Building
{
    /// <summary>
    /// The player half of the IFunctionalPlaceable contract from Building
    /// Phase 1: E (or an "Interact" input action) raycasts the shared camera
    /// aim ray and calls Interact on the functional placeable being looked at
    /// (campfire fueling/lighting, workbench menu, future storage/doors).
    ///
    /// AimedPrompt is refreshed every frame so a future HUD phase can render
    /// the "[E] Light campfire" hint without touching this logic — null while
    /// nothing interactable is aimed.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerReferences))]
    public sealed class PlayerInteraction : MonoBehaviour
    {
        [Tooltip("Maximum interaction distance from the camera, meters.")]
        [SerializeField] private float reach = 3.5f;

        private PlayerReferences references;
        private readonly RaycastHit[] rayBuffer = new RaycastHit[16];

        /// <summary>Prompt of the aimed functional placeable, null when none. For the future HUD.</summary>
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
            IFunctionalPlaceable placeable = FindAimedPlaceable();
            AimedPrompt = placeable?.InteractionPrompt;
        }

        private void TryInteract()
        {
            IFunctionalPlaceable placeable = FindAimedPlaceable();
            placeable?.Interact(gameObject);
        }

        private IFunctionalPlaceable FindAimedPlaceable()
        {
            if (!PlayerAimRaycast.Raycast(references.CameraPivot, transform, reach, rayBuffer, out RaycastHit hit))
                return null;

            return hit.collider.GetComponentInParent<IFunctionalPlaceable>();
        }
    }
}
