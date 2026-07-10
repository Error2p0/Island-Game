using UnityEngine;

namespace IslandGame.Building
{
    /// <summary>
    /// The contract for building pieces that DO something after being placed
    /// (campfire, workbench, storage, ...). Phase 3 implements the concrete
    /// behaviors against this interface; it exists now so those components
    /// plug into placement and interaction without retrofitting.
    ///
    /// CONVENTION: implementors are MonoBehaviours living on the placed
    /// prefab (root or child). BuildingPiece finds them with
    /// GetComponentsInChildren and calls Init exactly once — right after
    /// placement for placed pieces, from Start for pieces hand-authored into
    /// a scene. The player interaction system raycasts for BuildingPiece,
    /// shows InteractionPrompt, and calls Interact on use. A functional
    /// prefab additionally carries a CraftingStationMarker when it should
    /// satisfy recipe station requirements — that existing convention is
    /// unchanged and orthogonal to this interface.
    /// </summary>
    public interface IFunctionalPlaceable
    {
        /// <summary>UI prompt shown when the player aims at the piece (e.g. "Light campfire").</summary>
        string InteractionPrompt { get; }

        /// <summary>
        /// Called exactly once when the piece enters the world (placed by the
        /// player, or Start for scene-authored instances), after the owning
        /// BuildingPiece resolved its definition and health.
        /// </summary>
        void Init(BuildingPiece piece);

        /// <summary>Player used the piece. interactor is the player's root GameObject.</summary>
        void Interact(GameObject interactor);
    }
}
