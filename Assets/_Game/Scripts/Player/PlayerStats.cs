using System;
using IslandGame.Data.Items;
using IslandGame.Data.Stats;
using IslandGame.Inventory;
using IslandGame.Stats;
using UnityEngine;

namespace IslandGame.Player
{
    /// <summary>
    /// The player's needs-and-consumption façade, upgraded from the minimal
    /// hunger hook of the content phase to the stat system: hunger and thirst
    /// now LIVE in the StatContainer (their slow drain is authored as negative
    /// regen on the StatDefinitions, ticked by the container — the local drain
    /// loop only survives as a fallback for rigs without a container), while
    /// this component keeps owning the EAT action and the compatibility
    /// surface (Hunger01 / HungerChanged) the earlier phase promised.
    /// Starvation/dehydration consequences are PlayerSurvival's job, expressed
    /// as stat modifiers — never hardcoded here.
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
        [Header("Hunger (fallback mode only — stat-backed values come from the Hunger/Thirst StatDefinitions)")]
        [SerializeField] private float maxHunger = 100f;

        [Tooltip("Fallback only: hunger lost per real-time minute when no StatContainer is present. Stat-backed drain is the Hunger StatDefinition's negative regen.")]
        [Min(0f)]
        [SerializeField] private float hungerDrainPerMinute = 2.5f;

        private PlayerReferences references;
        private HotbarSelector selector;
        private InventorySystem inventory;
        private StatContainer statContainer;
        private bool hungerStatBacked;
        private float hunger;

        /// <summary>Current hunger, 0 (starving) .. 1 (full). Stat-backed when the container has a hunger stat.</summary>
        public float Hunger01 => hungerStatBacked
            ? statContainer.GetNormalized(StatIds.Hunger)
            : maxHunger > 0f ? hunger / maxHunger : 0f;

        /// <summary>Current thirst, 0 (dehydrated) .. 1 (full). Requires the stat container; 1 when absent.</summary>
        public float Thirst01 => statContainer != null ? statContainer.GetNormalized(StatIds.Thirst, 1f) : 1f;

        /// <summary>Raised whenever hunger changes (drain ticks and meals alike), with Hunger01.</summary>
        public event Action<float> HungerChanged;

        /// <summary>Raised whenever thirst changes, with Thirst01. Only fires when stat-backed.</summary>
        public event Action<float> ThirstChanged;

        private void Awake()
        {
            references = GetComponent<PlayerReferences>();
            selector = GetComponent<HotbarSelector>();
            inventory = GetComponent<InventorySystem>();
            statContainer = GetComponent<StatContainer>();
            hungerStatBacked = statContainer != null && statContainer.Has(StatIds.Hunger);
            hunger = maxHunger;
        }

        private void OnEnable()
        {
            references.InputHandler.PlacePressed += TryConsumeEquipped;
            if (statContainer != null)
                statContainer.OnStatChanged += OnStatChanged;
        }

        private void OnDisable()
        {
            references.InputHandler.PlacePressed -= TryConsumeEquipped;
            if (statContainer != null)
                statContainer.OnStatChanged -= OnStatChanged;
        }

        private void Update()
        {
            // Stat-backed drain is the container's regen tick — nothing to do here.
            if (hungerStatBacked)
                return;

            if (hungerDrainPerMinute <= 0f || hunger <= 0f)
                return;

            float previous = hunger;
            hunger = Mathf.Max(0f, hunger - hungerDrainPerMinute / 60f * Time.deltaTime);
            if (!Mathf.Approximately(previous, hunger))
                HungerChanged?.Invoke(Hunger01);
        }

        /// <summary>Re-raises the compatibility events for consumers wired before the stats phase (and any future ones that prefer this surface).</summary>
        private void OnStatChanged(string statId, float oldValue, float newValue)
        {
            if (statId == StatIds.Hunger)
                HungerChanged?.Invoke(Hunger01);
            else if (statId == StatIds.Thirst)
                ThirstChanged?.Invoke(Thirst01);
        }

        /// <summary>Restores hunger (meals, future potions). Clamped to the bar.</summary>
        public void RestoreHunger(float amount)
        {
            if (amount <= 0f)
                return;

            if (hungerStatBacked)
            {
                statContainer.Modify(StatIds.Hunger, amount);
                return; // event re-raised via OnStatChanged
            }

            hunger = Mathf.Min(maxHunger, hunger + amount);
            HungerChanged?.Invoke(Hunger01);
        }

        /// <summary>Restores thirst (drinks, juicy food). No-op without a stat container.</summary>
        public void RestoreThirst(float amount)
        {
            if (amount <= 0f || statContainer == null)
                return;

            statContainer.Modify(StatIds.Thirst, amount);
        }

        private void TryConsumeEquipped()
        {
            ItemDefinition equipped = selector != null ? selector.EquippedItem : null;
            if (equipped == null || equipped.Category != ItemCategory.Consumable)
                return;
            if (equipped.HungerRestore <= 0f && equipped.ThirstRestore <= 0f)
                return;

            if (inventory == null || inventory.ConsumeFromSlot(selector.SelectedIndex, 1) == 0)
                return;

            RestoreHunger(equipped.HungerRestore);
            RestoreThirst(equipped.ThirstRestore);
            Debug.Log(
                $"Consumed {equipped.DisplayName} (+{equipped.HungerRestore} hunger, +{equipped.ThirstRestore} thirst " +
                $"→ hunger {Hunger01:P0}, thirst {Thirst01:P0}).");
        }
    }
}
