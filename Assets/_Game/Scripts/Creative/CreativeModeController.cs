using UnityEngine;

namespace IslandGame.Creative
{
    /// <summary>
    /// The creative-mode gate. Lives on the player; every creative feature
    /// (the spawn menu now, possible future cheats like free placement) checks
    /// CreativeModeEnabled before doing anything. Untick it on the player for
    /// survival builds/testing and the whole creative layer goes inert — no
    /// code deleted, the menu simply refuses to open (and force-closes if it
    /// was open when the flag went off).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CreativeModeController : MonoBehaviour
    {
        [Tooltip("Master switch for all creative features. Off = survival: the creative menu cannot open.")]
        [SerializeField] private bool creativeModeEnabled = true;

        public bool CreativeModeEnabled
        {
            get => creativeModeEnabled;
            set => creativeModeEnabled = value;
        }
    }
}
