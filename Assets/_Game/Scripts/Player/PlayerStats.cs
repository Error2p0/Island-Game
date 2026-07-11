using System;
using IslandGame.Data.Items;
using IslandGame.Inventory;
using UnityEngine;

namespace IslandGame.Player
{
    /// <summary>
    /// The minimal survival-stat component the consumable content needed:
    /// hunger drains slowly in real time, and the use/place button EATS the
    /// equipped Consumable item (one unit from the equipped stack →
    /// ItemDefinition.HungerRestore back onto the bar). Pure state + events —
    /// a HUD phase renders Hunger01/HungerChanged; starvation consequences
    /// (health damage, stamina caps) are deliberately future scope and noted,
    /// not stubbed: today the stat exists, drains, restores, and is queryable.
    ///
    /// The eat action shares the place button on purpose: block-placement
    /// ignores Consumables (no PlacedBlock/PlacedPiece) and this ignores
    /// everything else, so the two consumers stay disjoint — same pattern as
    /// block-vs-piece placement.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerReferences))]
    public sealed class PlayerStats : MonoBehaviour
    {
        [Header("Hunger")]
        [SerializeField] private float maxHunger = 100f;

        [Tooltip("Hunger lost per real-time minute. 2.5 ≈ one food item every few day/night cycles at default day length.")]
        [Min(0f)]
        [SerializeField] private float hungerDrainPerMinute = 2.5f;

        private PlayerReferences references;
        private HotbarSelector selector;
        private InventorySystem inventory;
        private float hunger;

        /// <summary>Current hunger, 0 (starving) .. 1 (full). The future HUD reads this.</summary>
        public float Hunger01 => maxHunger > 0f ? hunger / maxHunger : 0f;

        /// <summary>Raised whenever hunger changes (drain ticks and meals alike), with Hunger01.</summary>
        public event Action<float> HungerChanged;

        private void Awake()
        {
            references = GetComponent<PlayerReferences>();
            selector = GetComponent<HotbarSelector>();
            inventory = GetComponent<InventorySystem>();
            hunger = maxHunger;
        }

        private void OnEnable()
        {
            references.InputHandler.PlacePressed += TryConsumeEquipped;
        }

        private void OnDisable()
        {
            references.InputHandler.PlacePressed -= TryConsumeEquipped;
        }

        private void Update()
        {
            if (hungerDrainPerMinute <= 0f || hunger <= 0f)
                return;

            float previous = hunger;
            hunger = Mathf.Max(0f, hunger - hungerDrainPerMinute / 60f * Time.deltaTime);
            if (!Mathf.Approximately(previous, hunger))
                HungerChanged?.Invoke(Hunger01);
        }

        /// <summary>Restores hunger (meals, future potions). Clamped to the bar.</summary>
        public void RestoreHunger(float amount)
        {
            if (amount <= 0f)
                return;

            hunger = Mathf.Min(maxHunger, hunger + amount);
            HungerChanged?.Invoke(Hunger01);
        }

        private void TryConsumeEquipped()
        {
            ItemDefinition equipped = selector != null ? selector.EquippedItem : null;
            if (equipped == null || equipped.Category != ItemCategory.Consumable || equipped.HungerRestore <= 0f)
                return;

            if (inventory == null || inventory.ConsumeFromSlot(selector.SelectedIndex, 1) == 0)
                return;

            RestoreHunger(equipped.HungerRestore);
            Debug.Log($"Ate {equipped.DisplayName} (+{equipped.HungerRestore} hunger → {Hunger01:P0}).");
        }
    }
}
