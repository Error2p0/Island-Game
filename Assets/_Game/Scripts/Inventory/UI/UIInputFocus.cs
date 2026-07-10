using IslandGame.Player;
using UnityEngine;

namespace IslandGame.Inventory.UI
{
    /// <summary>
    /// Refcounted UI focus over gameplay input and the cursor, shared by every
    /// screen-covering panel (inventory, creative menu, future menus). Each
    /// open panel holds one acquisition; gameplay input unblocks and the cursor
    /// re-locks only when the LAST panel closes — so panels never need to know
    /// about each other and close-order can't corrupt the state.
    /// </summary>
    public static class UIInputFocus
    {
        private static int focusCount;

        public static bool AnyFocus => focusCount > 0;

        public static void Acquire(PlayerInputHandler input)
        {
            focusCount++;
            Apply(input);
        }

        public static void Release(PlayerInputHandler input)
        {
            focusCount = Mathf.Max(0, focusCount - 1);
            Apply(input);
        }

        /// <summary>
        /// Call each frame from an open panel: re-asserts the unlocked cursor
        /// against the camera controller's focus re-lock (which must keep
        /// working for normal gameplay — this is the cheaper side to repeat).
        /// </summary>
        public static void EnforceCursor()
        {
            if (focusCount > 0 && Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private static void Apply(PlayerInputHandler input)
        {
            bool focused = focusCount > 0;

            if (input != null)
                input.GameplayBlocked = focused;

            Cursor.lockState = focused ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = focused;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Domain-reload-disabled play mode: statics survive, so reset by hand.
            focusCount = 0;
        }
    }
}
