using UnityEngine;

namespace IslandGame.Data.Items
{
    /// <summary>
    /// Which upper-body pose the item requests while held. Maps to poses on the
    /// Animator's upper-body item layer (reserved by the movement system's
    /// PlayerAnimationBuilder); the held-item integration phase reads this to
    /// pick the pose. Same extension rule as all data enums: append only,
    /// never reorder or renumber.
    /// </summary>
    public enum HoldType
    {
        /// <summary>Item cannot be held in hands (upper-body layer stays at weight 0).</summary>
        None = 0,
        OneHanded = 1,
        TwoHanded = 2,

        /// <summary>Held in the secondary hand (shields, torches alongside a main-hand item).</summary>
        OffHand = 3,
    }

    /// <summary>
    /// Which hand socket the held item's world model attaches to. The socket
    /// transforms themselves are created on the rig in the held-item phase;
    /// data only records the intent. Append only.
    /// </summary>
    public enum HoldSocket
    {
        RightHand = 0,
        LeftHand = 1,
    }

    /// <summary>
    /// THE HOLD SOCKET CONVENTION (the held-item phase implements it; the Item
    /// Editor authors against it):
    ///   - The rig gets one child transform per HoldSocket value, parented to
    ///     the matching hand bone, named by GetSocketTransformName. The socket
    ///     transform itself is aligned to the palm; per-item fit lives on the
    ///     item, never on the socket.
    ///   - Equipping instantiates ItemDefinition.WorldModelPrefab as a child of
    ///     that socket and sets localPosition = HoldLocalPosition,
    ///     localRotation = HoldLocalRotation, localScale = one.
    ///   - The Item Editor previews exactly this transform (model offset
    ///     relative to socket axes), so numbers authored there transfer 1:1.
    /// </summary>
    public static class HoldSocketConvention
    {
        public const string RightHandSocketName = "Socket_RightHand";
        public const string LeftHandSocketName = "Socket_LeftHand";

        /// <summary>Two-handed centered reference point, parented to the chest (aiming/pose reference for two-handed items).</summary>
        public const string TwoHandedSocketName = "Socket_TwoHanded";

        /// <summary>
        /// Optional child transform ON THE WORLD-MODEL PREFAB marking where the
        /// off-hand grips a two-handed item (e.g. lower on an axe handle). When
        /// absent, the hold controller falls back to a configurable offset
        /// below the item's origin.
        /// </summary>
        public const string OffHandGripName = "OffHandGrip";

        /// <summary>Name of the socket transform on the rig for a given socket value.</summary>
        public static string GetSocketTransformName(HoldSocket socket)
        {
            return socket == HoldSocket.LeftHand ? LeftHandSocketName : RightHandSocketName;
        }
    }
}
