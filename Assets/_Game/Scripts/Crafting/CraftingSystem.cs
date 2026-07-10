using System;
using IslandGame.Data.Crafting;
using IslandGame.Inventory;
using UnityEngine;

namespace IslandGame.Crafting
{
    /// <summary>
    /// Executes recipes against the player's inventory. Pure logic + events;
    /// the crafting menu is just a view over this.
    ///
    /// SAFETY ORDER: everything is validated BEFORE anything is consumed —
    /// ingredients present, station near, and crucially the output must fit
    /// (InventorySystem.CanFitItem) so a full inventory can never eat
    /// ingredients and drop nothing back. The fit check is deliberately
    /// conservative: space freed by consuming the ingredients themselves is
    /// not counted (per the phase requirement — free a slot if it blocks you).
    /// As a last-resort guard against same-frame races, output that still
    /// fails to fit after consumption spawns as a WorldItem — never deleted.
    ///
    /// TIMED CRAFTS consume at COMPLETION, not at start: walking away from
    /// the station or spending the ingredients mid-craft simply aborts with a
    /// reason, nothing is lost or refunded because nothing was taken.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(InventorySystem))]
    public sealed class CraftingSystem : MonoBehaviour
    {
        [Tooltip("How close a CraftingStationMarker must be to count as 'at the station', meters.")]
        [SerializeField] private float stationSearchRadius = 3f;

        /// <summary>Raised after a craft's output lands in the inventory.</summary>
        public event Action<RecipeDefinition> CraftCompleted;

        /// <summary>Raised when a timed craft aborts at completion-time revalidation (reason in LastError).</summary>
        public event Action<RecipeDefinition> CraftAborted;

        public bool IsCrafting => activeRecipe != null;
        public RecipeDefinition ActiveRecipe => activeRecipe;

        public float Progress01 =>
            activeRecipe == null || activeRecipe.CraftSeconds <= 0f
                ? 0f
                : Mathf.Clamp01(craftTimer / activeRecipe.CraftSeconds);

        /// <summary>Human-readable reason for the last failed start/abort — the menu surfaces this.</summary>
        public string LastError { get; private set; } = string.Empty;

        private readonly Collider[] stationBuffer = new Collider[16];

        private InventorySystem inventory;
        private RecipeDefinition activeRecipe;
        private float craftTimer;

        private void Awake()
        {
            inventory = GetComponent<InventorySystem>();
        }

        private void Update()
        {
            if (activeRecipe == null)
                return;

            craftTimer += Time.deltaTime;
            if (craftTimer < activeRecipe.CraftSeconds)
                return;

            RecipeDefinition finished = activeRecipe;
            activeRecipe = null;

            // Conditions may have changed during the timer (walked away from
            // the station, dropped ingredients) — revalidate, then execute.
            if (CanCraft(finished, out string reason))
            {
                Execute(finished);
            }
            else
            {
                LastError = reason;
                CraftAborted?.Invoke(finished);
            }
        }

        /// <summary>Full pre-flight check; reason explains the first failure for the UI.</summary>
        public bool CanCraft(RecipeDefinition recipe, out string reason)
        {
            if (IsCrafting)
            {
                reason = "Already crafting.";
                return false;
            }

            if (!ValidateRequirements(recipe, out reason))
                return false;

            // Building recipes produce no inventory item — nothing to fit.
            if (!recipe.IsBuildingRecipe && !inventory.CanFitItem(recipe.Output, recipe.OutputCount))
            {
                reason = "No room for the output — free up inventory space first.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        /// <summary>
        /// The shared requirement check (dangling refs, station proximity,
        /// ingredient availability) used by hand crafting AND by building
        /// placement — the one implementation of "can this recipe be paid".
        /// </summary>
        public bool ValidateRequirements(RecipeDefinition recipe, out string reason)
        {
            if (recipe == null || recipe.HasDanglingReferences)
            {
                reason = "Recipe has missing items — fix it in the Recipe Editor.";
                return false;
            }

            if (recipe.Station != CraftingStationType.None && !IsNearStation(recipe.Station))
            {
                reason = $"Requires a {recipe.Station} nearby.";
                return false;
            }

            foreach (RecipeIngredient ingredient in recipe.Ingredients)
            {
                int have = inventory.GetItemCount(ingredient.Item);
                if (have < ingredient.Count)
                {
                    reason = $"Missing {ingredient.Count - have} × {ingredient.Item.DisplayName}.";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

        /// <summary>
        /// Building placement's pay-at-confirm entry point: revalidates the
        /// requirements and consumes the ingredients atomically — nothing is
        /// taken on any failure. No output is granted here by design; the
        /// caller places the piece into the world.
        /// </summary>
        public bool TryConsumeIngredientsFor(RecipeDefinition recipe, out string reason)
        {
            if (!ValidateRequirements(recipe, out reason))
            {
                LastError = reason;
                return false;
            }

            foreach (RecipeIngredient ingredient in recipe.Ingredients)
                inventory.RemoveItem(ingredient.Item, ingredient.Count);

            LastError = string.Empty;
            return true;
        }

        /// <summary>Ingredient-availability check only (the menu's craftable-only filter — station/space shown separately).</summary>
        public bool HasIngredientsFor(RecipeDefinition recipe)
        {
            if (recipe == null || recipe.HasDanglingReferences)
                return false;

            foreach (RecipeIngredient ingredient in recipe.Ingredients)
            {
                if (inventory.GetItemCount(ingredient.Item) < ingredient.Count)
                    return false;
            }

            return true;
        }

        /// <summary>Starts the craft: instant recipes execute immediately, timed ones tick in Update.</summary>
        public bool TryStartCraft(RecipeDefinition recipe, out string reason)
        {
            if (recipe != null && recipe.IsBuildingRecipe)
            {
                // Building recipes place, they never stack in inventory — the
                // menu arms BuildingPlacementController instead of calling this.
                reason = "Building recipes are placed in the world, not crafted into the inventory.";
                LastError = reason;
                return false;
            }

            if (!CanCraft(recipe, out reason))
            {
                LastError = reason;
                return false;
            }

            if (recipe.CraftSeconds <= 0f)
            {
                Execute(recipe);
                return true;
            }

            activeRecipe = recipe;
            craftTimer = 0f;
            return true;
        }

        public void CancelCraft()
        {
            activeRecipe = null; // nothing was consumed yet, so nothing to refund
        }

        /// <summary>True when a matching station marker is within reach of the player.</summary>
        public bool IsNearStation(CraftingStationType station)
        {
            int hitCount = Physics.OverlapSphereNonAlloc(
                transform.position + Vector3.up * 0.9f, stationSearchRadius, stationBuffer,
                ~0, QueryTriggerInteraction.Collide);

            for (int i = 0; i < hitCount; i++)
            {
                var marker = stationBuffer[i].GetComponentInParent<CraftingStationMarker>();
                if (marker != null && marker.StationType == station)
                    return true;
            }

            return false;
        }

        private void Execute(RecipeDefinition recipe)
        {
            foreach (RecipeIngredient ingredient in recipe.Ingredients)
                inventory.RemoveItem(ingredient.Item, ingredient.Count);

            int added = inventory.AddItem(recipe.Output, recipe.OutputCount);
            int leftover = recipe.OutputCount - added;
            if (leftover > 0)
            {
                // Should be unreachable behind CanCraft's fit pre-check; if a
                // race beat us anyway, the output lands at the player's feet.
                WorldItem.Spawn(recipe.Output, leftover, 1f,
                    transform.position + Vector3.up * 1.2f + transform.forward * 0.6f,
                    transform.forward * 1.5f + Vector3.up);
            }

            LastError = string.Empty;
            CraftCompleted?.Invoke(recipe);
        }
    }
}
