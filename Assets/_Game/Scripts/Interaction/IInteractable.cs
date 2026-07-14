using UnityEngine;

namespace IslandGame.Interaction
{
    /// <summary>
    /// The one "press E on the thing you're aiming at" contract. Extracted
    /// from IFunctionalPlaceable in the foliage phase — the prompt + Interact
    /// half of that interface was never building-specific, and harvestable
    /// foliage needs exactly it without the BuildingPiece lifecycle.
    /// IFunctionalPlaceable now extends this, so every existing behavior
    /// (campfire, door, chest, ...) and every new world interactable flow
    /// through the SAME PlayerInteraction raycast, prompt and input path —
    /// there is deliberately no second interact system.
    ///
    /// CONVENTION: implementors are MonoBehaviours on (or under) an object
    /// with a non-trigger collider — PlayerInteraction resolves the aimed
    /// collider with GetComponentInParent. Return null from InteractionPrompt
    /// to mean "nothing to do right now" (a depleted bush, a full campfire):
    /// the HUD shows no hint and Interact is expected to no-op.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>UI prompt shown when the player aims at the object (e.g. "Light campfire", "Harvest Berry Bush"). Null = not currently interactable.</summary>
        string InteractionPrompt { get; }

        /// <summary>Player used the object. interactor is the player's root GameObject.</summary>
        void Interact(GameObject interactor);
    }
}
