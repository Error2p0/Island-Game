using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace IslandGame.Player
{
    /// <summary>
    /// Sole owner of the Input System asset. Exposes clean polled values and
    /// press events; contains zero movement/camera logic. Later phases add new
    /// actions to the same asset and surface them here.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInputHandler : MonoBehaviour
    {
        [SerializeField] private InputActionAsset actions;
        [SerializeField] private string actionMapName = "Player";

        /// <summary>Raised on the frame Jump is pressed.</summary>
        public event Action JumpPressed;

        /// <summary>Raised on the frame Crouch is pressed.</summary>
        public event Action CrouchPressed;

        /// <summary>Raised on the frame Prone is pressed.</summary>
        public event Action PronePressed;

        /// <summary>Raised when the inventory toggle is pressed. NOT gated by GameplayBlocked — closing must always work.</summary>
        public event Action InventoryTogglePressed;

        /// <summary>Raised when the creative-menu toggle is pressed. NOT gated by GameplayBlocked — closing must always work.</summary>
        public event Action CreativeMenuTogglePressed;

        /// <summary>Raised when the crafting-menu toggle is pressed (fallback key B — C belongs to Crouch). NOT gated by GameplayBlocked.</summary>
        public event Action CraftingTogglePressed;

        /// <summary>Raised when the drop-item key is pressed (gated by GameplayBlocked).</summary>
        public event Action DropPressed;

        /// <summary>Raised with the 0-based hotbar index when a number key 1-9 is pressed (gated by GameplayBlocked).</summary>
        public event Action<int> HotbarSlotPressed;

        /// <summary>
        /// True while UI owns the input: gameplay values read as zero/false and
        /// gameplay press events are suppressed, so movement, look, and actions
        /// freeze without any other system needing UI logic. Set by UIInputFocus
        /// (every screen-covering panel); ALSO true on its own while a UI text
        /// field is being edited, so a text field can never leak keystrokes
        /// into gameplay even outside a blocking panel.
        /// </summary>
        public bool GameplayBlocked
        {
            get => blockedByPanel || IsTextInputFocused;
            set => blockedByPanel = value;
        }

        private bool blockedByPanel;

        /// <summary>
        /// True while a uGUI InputField is being edited (caret active) — the
        /// keyboard belongs to the text field. The always-available menu
        /// toggles consult this too: typing "b"/"i"/Tab into a search box must
        /// never toggle a menu. Cached per frame; the EventSystem is the one
        /// source of truth, so every current and future text field is covered
        /// without per-field wiring.
        /// </summary>
        public static bool IsTextInputFocused
        {
            get
            {
                if (Time.frameCount != textFocusFrame)
                {
                    textFocusFrame = Time.frameCount;
                    textFocusCached = ComputeTextInputFocused();
                }

                return textFocusCached;
            }
        }

        private static int textFocusFrame = -1;
        private static bool textFocusCached;

        private static bool ComputeTextInputFocused()
        {
            EventSystem eventSystem = EventSystem.current;
            GameObject selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
            if (selected == null)
                return false;

            var field = selected.GetComponent<InputField>();
            return field != null && field.isFocused;
        }

        public Vector2 MoveInput =>
            !GameplayBlocked && moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;

        public Vector2 LookInput =>
            !GameplayBlocked && lookAction != null ? lookAction.ReadValue<Vector2>() : Vector2.zero;

        public bool SprintHeld => !GameplayBlocked && sprintAction != null && sprintAction.IsPressed();
        public bool CrouchHeld => !GameplayBlocked && crouchAction != null && crouchAction.IsPressed();
        public bool JumpHeld => !GameplayBlocked && jumpAction != null && jumpAction.IsPressed();

        /// <summary>Raw mouse scroll this frame (y). Hotbar cycling reads this; zero while GameplayBlocked.</summary>
        public float HotbarScrollDelta =>
            !GameplayBlocked && Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;

        /// <summary>True while the mine button is held (optional "Mine" action, else left mouse). Gated by GameplayBlocked.</summary>
        public bool MineHeld =>
            !GameplayBlocked && (mineAction != null
                ? mineAction.IsPressed()
                : Mouse.current != null && Mouse.current.leftButton.isPressed);

        /// <summary>True while the deconstruct button is held (optional "Deconstruct" action, else middle mouse — Valheim's remove binding). Gated by GameplayBlocked. Deconstruction is a timed hold, so this is polled like MineHeld rather than raised as a press event.</summary>
        public bool DeconstructHeld =>
            !GameplayBlocked && (deconstructAction != null
                ? deconstructAction.IsPressed()
                : Mouse.current != null && Mouse.current.middleButton.isPressed);

        /// <summary>True while the free-place modifier is held (optional "FreePlace" action, else Left Alt) — building placement suppresses socket snapping and grid rounding while this is down. Gated by GameplayBlocked.</summary>
        public bool FreePlaceHeld =>
            !GameplayBlocked && (freePlaceAction != null
                ? freePlaceAction.IsPressed()
                : Keyboard.current != null && Keyboard.current.leftAltKey.isPressed);

        /// <summary>Raised on the frame the place button is pressed (optional "Place" action, else right mouse). Gated by GameplayBlocked.</summary>
        public event Action PlacePressed;

        /// <summary>Raised when the rotate-building-piece key is pressed (optional "RotatePiece" action, else R). Gated by GameplayBlocked.</summary>
        public event Action RotatePiecePressed;

        /// <summary>Raised when the interact key is pressed (optional "Interact" action, else E). Gated by GameplayBlocked.</summary>
        public event Action InteractPressed;

        /// <summary>
        /// True when the current Look value comes from a pointer (mouse) delta.
        /// Pointer deltas are already per-frame, so consumers must NOT scale them
        /// by deltaTime; gamepad stick values must be scaled by deltaTime.
        /// </summary>
        public bool LookInputIsPointerDelta =>
            lookAction == null ||
            lookAction.activeControl == null ||
            lookAction.activeControl.device is Pointer;

        private InputActionMap gameplayMap;
        private InputAction moveAction;
        private InputAction lookAction;
        private InputAction sprintAction;
        private InputAction crouchAction;
        private InputAction proneAction;
        private InputAction jumpAction;
        private InputAction toggleInventoryAction;
        private InputAction dropAction;
        private InputAction toggleCreativeMenuAction;
        private InputAction mineAction;
        private InputAction placeAction;
        private InputAction toggleCraftingAction;
        private InputAction rotatePieceAction;
        private InputAction deconstructAction;
        private InputAction interactAction;
        private InputAction freePlaceAction;

        private void Awake()
        {
            if (actions == null)
            {
                Debug.LogError("[PlayerInputHandler] No InputActionAsset assigned.", this);
                enabled = false;
                return;
            }

            gameplayMap = actions.FindActionMap(actionMapName, throwIfNotFound: true);
            moveAction = gameplayMap.FindAction("Move", throwIfNotFound: true);
            lookAction = gameplayMap.FindAction("Look", throwIfNotFound: true);
            sprintAction = gameplayMap.FindAction("Sprint", throwIfNotFound: true);
            crouchAction = gameplayMap.FindAction("Crouch", throwIfNotFound: true);
            proneAction = gameplayMap.FindAction("Prone", throwIfNotFound: true);
            jumpAction = gameplayMap.FindAction("Jump", throwIfNotFound: true);

            // Inventory-era actions are OPTIONAL in the asset: when absent, the
            // Update() fallback polls the keyboard directly (Tab/I, G) so no
            // manual asset editing is required. Add "ToggleInventory" / "DropItem"
            // actions to the map to take over the bindings properly.
            toggleInventoryAction = gameplayMap.FindAction("ToggleInventory", throwIfNotFound: false);
            dropAction = gameplayMap.FindAction("DropItem", throwIfNotFound: false);
            toggleCreativeMenuAction = gameplayMap.FindAction("ToggleCreativeMenu", throwIfNotFound: false);
            mineAction = gameplayMap.FindAction("Mine", throwIfNotFound: false);
            placeAction = gameplayMap.FindAction("Place", throwIfNotFound: false);
            toggleCraftingAction = gameplayMap.FindAction("ToggleCrafting", throwIfNotFound: false);
            rotatePieceAction = gameplayMap.FindAction("RotatePiece", throwIfNotFound: false);
            deconstructAction = gameplayMap.FindAction("Deconstruct", throwIfNotFound: false);
            interactAction = gameplayMap.FindAction("Interact", throwIfNotFound: false);
            freePlaceAction = gameplayMap.FindAction("FreePlace", throwIfNotFound: false);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            // A focused text field owns every keystroke — even the menu
            // toggles below, which otherwise ignore GameplayBlocked.
            if (IsTextInputFocused)
                return;

            if (toggleInventoryAction == null
                && (keyboard.tabKey.wasPressedThisFrame || keyboard.iKey.wasPressedThisFrame))
                InventoryTogglePressed?.Invoke();

            if (toggleCreativeMenuAction == null && keyboard.f1Key.wasPressedThisFrame)
                CreativeMenuTogglePressed?.Invoke();

            if (toggleCraftingAction == null && keyboard.bKey.wasPressedThisFrame)
                CraftingTogglePressed?.Invoke();

            if (GameplayBlocked)
                return;

            if (dropAction == null && keyboard.gKey.wasPressedThisFrame)
                DropPressed?.Invoke();

            if (placeAction == null && Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
                PlacePressed?.Invoke();

            if (rotatePieceAction == null && keyboard.rKey.wasPressedThisFrame)
                RotatePiecePressed?.Invoke();

            if (interactAction == null && keyboard.eKey.wasPressedThisFrame)
                InteractPressed?.Invoke();

            // Hotbar digits are always keyboard-polled (no per-slot actions in the asset).
            for (int i = 0; i < HotbarDigitKeys.Length; i++)
            {
                if (keyboard[HotbarDigitKeys[i]].wasPressedThisFrame)
                    HotbarSlotPressed?.Invoke(i);
            }
        }

        private static readonly Key[] HotbarDigitKeys =
        {
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5,
            Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9,
        };

        private void OnEnable()
        {
            if (gameplayMap == null)
                return;

            jumpAction.performed += OnJumpPerformed;
            crouchAction.performed += OnCrouchPerformed;
            proneAction.performed += OnPronePerformed;
            if (toggleInventoryAction != null)
                toggleInventoryAction.performed += OnToggleInventoryPerformed;
            if (dropAction != null)
                dropAction.performed += OnDropPerformed;
            if (toggleCreativeMenuAction != null)
                toggleCreativeMenuAction.performed += OnToggleCreativeMenuPerformed;
            if (placeAction != null)
                placeAction.performed += OnPlacePerformed;
            if (toggleCraftingAction != null)
                toggleCraftingAction.performed += OnToggleCraftingPerformed;
            if (rotatePieceAction != null)
                rotatePieceAction.performed += OnRotatePiecePerformed;
            if (interactAction != null)
                interactAction.performed += OnInteractPerformed;
            gameplayMap.Enable();
        }

        private void OnDisable()
        {
            if (gameplayMap == null)
                return;

            gameplayMap.Disable();
            jumpAction.performed -= OnJumpPerformed;
            crouchAction.performed -= OnCrouchPerformed;
            proneAction.performed -= OnPronePerformed;
            if (toggleInventoryAction != null)
                toggleInventoryAction.performed -= OnToggleInventoryPerformed;
            if (dropAction != null)
                dropAction.performed -= OnDropPerformed;
            if (toggleCreativeMenuAction != null)
                toggleCreativeMenuAction.performed -= OnToggleCreativeMenuPerformed;
            if (placeAction != null)
                placeAction.performed -= OnPlacePerformed;
            if (toggleCraftingAction != null)
                toggleCraftingAction.performed -= OnToggleCraftingPerformed;
            if (rotatePieceAction != null)
                rotatePieceAction.performed -= OnRotatePiecePerformed;
            if (interactAction != null)
                interactAction.performed -= OnInteractPerformed;
        }

        private void OnJumpPerformed(InputAction.CallbackContext context)
        {
            if (!GameplayBlocked)
                JumpPressed?.Invoke();
        }

        private void OnCrouchPerformed(InputAction.CallbackContext context)
        {
            if (!GameplayBlocked)
                CrouchPressed?.Invoke();
        }

        private void OnPronePerformed(InputAction.CallbackContext context)
        {
            if (!GameplayBlocked)
                PronePressed?.Invoke();
        }

        // The toggle handlers ignore GameplayBlocked (closing a menu must
        // always work) but NOT text focus — keystrokes typed into a field
        // may never toggle a menu.
        private void OnToggleInventoryPerformed(InputAction.CallbackContext context)
        {
            if (!IsTextInputFocused)
                InventoryTogglePressed?.Invoke();
        }

        private void OnToggleCreativeMenuPerformed(InputAction.CallbackContext context)
        {
            if (!IsTextInputFocused)
                CreativeMenuTogglePressed?.Invoke();
        }

        private void OnToggleCraftingPerformed(InputAction.CallbackContext context)
        {
            if (!IsTextInputFocused)
                CraftingTogglePressed?.Invoke();
        }

        private void OnDropPerformed(InputAction.CallbackContext context)
        {
            if (!GameplayBlocked)
                DropPressed?.Invoke();
        }

        private void OnPlacePerformed(InputAction.CallbackContext context)
        {
            if (!GameplayBlocked)
                PlacePressed?.Invoke();
        }

        private void OnRotatePiecePerformed(InputAction.CallbackContext context)
        {
            if (!GameplayBlocked)
                RotatePiecePressed?.Invoke();
        }

        private void OnInteractPerformed(InputAction.CallbackContext context)
        {
            if (!GameplayBlocked)
                InteractPressed?.Invoke();
        }
    }
}
