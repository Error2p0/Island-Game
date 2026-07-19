using System.Collections.Generic;
using IslandGame.Creatures.UI;
using IslandGame.Data.Stats;
using IslandGame.Interaction;
using IslandGame.Inventory;
using IslandGame.Player;
using IslandGame.Stats;
using UnityEngine;

namespace IslandGame.Creatures
{
    /// <summary>
    /// The taming half of a tameable creature — attached automatically by
    /// Creature.Init for species flagged Tameable (no prefab rebuilds), and
    /// the player's touchpoint through the SAME IInteractable aim/E path
    /// foliage and functional placeables use:
    ///
    ///   WILD  — interacting while holding a favorite food consumes one and
    ///     advances the deterministic feed count (progress lives honestly in
    ///     the prompt: "Feed Wolf (2/4)"); without the food the prompt says
    ///     what the species wants. The count, not a chance roll, ON PURPOSE:
    ///     the cost is plannable and the indicator can never lie.
    ///
    ///   TAME  — on the final feeding: spawner ownership released (a
    ///     companion is a persistent individual, never pooled/respawned
    ///     wildlife), the definition's tamed stat modifiers apply through the
    ///     standard StatModifier system (source = this component), the AI
    ///     enters the tamed branch, and the naming panel opens.
    ///
    ///   TAMED — interacting cycles Follow → Stay → Assist-if-capable (the
    ///     interact-cycle command surface, the project's established
    ///     interact-toggle idiom — doors and campfires already work this
    ///     way); interacting WITH food instead hand-feeds a wounded
    ///     companion back to health.
    ///
    ///   DEATH — a dead companion is mourned, not recycled: a name-tagged
    ///     message shows, it leaves the registry (so saves drop it), and
    ///     because ownership was released at tame time the body Destroys
    ///     instead of pooling; no spawner ever repopulates it.
    ///
    /// TamedCompanions is the live registry (campfire-registry pattern) the
    /// save system walks — this is the persistence case SavedCreature was
    /// reserved for.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Creature))]
    public sealed class CreatureTaming : MonoBehaviour, IInteractable
    {
        private const float FeedHealFraction = 0.25f;

        private static readonly List<CreatureTaming> tamedCompanions = new List<CreatureTaming>();
        private static PlayerReferences cachedPlayer;
        private static HotbarSelector cachedSelector;

        /// <summary>Every living tamed companion (the save system's gather source).</summary>
        public static IReadOnlyList<CreatureTaming> TamedCompanions => tamedCompanions;

        private Creature creature;
        private CreatureAI ai;
        private int feedingsDone;
        private float nextFeedTime;

        /// <summary>The player-given name (species name until renamed).</summary>
        public string CompanionName { get; private set; }

        public Creature Creature
        {
            get
            {
                if (creature == null)
                    creature = GetComponent<Creature>();
                return creature;
            }
        }

        /// <summary>Current command mode (persisted by the save system).</summary>
        public CompanionMode Mode => ai != null ? ai.CompanionMode : CompanionMode.Follow;

        private void Awake()
        {
            creature = GetComponent<Creature>();
            ai = GetComponent<CreatureAI>();
        }

        private void OnDestroy()
        {
            tamedCompanions.Remove(this);
        }

        /// <summary>Pooled-reuse reset (called from Creature.Init): a fresh wild body carries no old progress or registry entry.</summary>
        public void ResetWild()
        {
            feedingsDone = 0;
            nextFeedTime = 0f;
            CompanionName = null;
            tamedCompanions.Remove(this);
        }

        // ------------------------------------------------------------------
        // IInteractable — feeding, then commanding
        // ------------------------------------------------------------------

        public string InteractionPrompt
        {
            get
            {
                var definition = Creature.Definition;
                if (definition == null || Creature.IsDead)
                    return null;

                if (!Creature.IsTamed)
                {
                    if (!definition.Tameable)
                        return null;

                    string progress = $"({feedingsDone}/{definition.FeedingsToTame})";
                    return PlayerHoldsFavoriteFood(definition)
                        ? $"Feed {definition.DisplayName} {progress}"
                        : $"Tame with {FavoriteFoodNames(definition)} {progress}";
                }

                if (PlayerHoldsFavoriteFood(definition))
                    return $"Feed {CompanionName}";

                return $"Command {CompanionName}: {Mode} ▸ {NextMode()}";
            }
        }

        public void Interact(GameObject interactor)
        {
            var definition = Creature.Definition;
            if (definition == null || Creature.IsDead)
                return;

            var selector = interactor.GetComponent<HotbarSelector>();
            var inventory = interactor.GetComponent<InventorySystem>();
            bool holdingFood = selector != null && inventory != null
                && definition.IsFavoriteFood(selector.EquippedItem);

            if (!Creature.IsTamed)
            {
                if (!definition.Tameable || !holdingFood || Time.time < nextFeedTime)
                    return;

                inventory.ConsumeFromSlot(selector.SelectedIndex, 1);
                nextFeedTime = Time.time + definition.FeedCooldownSeconds;
                feedingsDone++;

                if (feedingsDone >= definition.FeedingsToTame)
                {
                    Tame();
                }
                else
                {
                    CompanionUIView.Instance?.ShowMessage(
                        $"{definition.DisplayName} trusts you a little more ({feedingsDone}/{definition.FeedingsToTame}).", 2.5f);
                }

                return;
            }

            // Tamed: food heals, empty (or non-food) hand commands.
            if (holdingFood)
            {
                float max = Creature.Stats.GetModifiedValue(StatIds.Health);
                if (Creature.Stats.GetValue(StatIds.Health) >= max - 0.01f)
                    return; // full — don't silently eat the snack

                inventory.ConsumeFromSlot(selector.SelectedIndex, 1);
                Creature.Stats.Modify(StatIds.Health, max * FeedHealFraction);
                CompanionUIView.Instance?.ShowMessage($"{CompanionName} wolfs it down.", 2f);
                return;
            }

            CycleMode();
        }

        // ------------------------------------------------------------------
        // Taming & commands
        // ------------------------------------------------------------------

        private void Tame()
        {
            var definition = Creature.Definition;

            Creature.IsTamed = true;
            Creature.OwnerSpawner?.ReleaseOwnership(Creature);
            ApplyTamedModifiers();

            CompanionName = definition.DisplayName;
            gameObject.name = $"Companion_{definition.Id}";
            Creature.OnDeath += OnCompanionDeath;
            tamedCompanions.Add(this);

            ai?.EnterTamedMode(CompanionMode.Follow);

            Debug.Log($"[Taming] {definition.DisplayName} tamed.", this);
            if (CompanionUIView.Instance != null)
                CompanionUIView.Instance.PromptForName(this);
        }

        /// <summary>Load path: rebuilds the tamed state without ceremony (no naming panel, no messages).</summary>
        public void RestoreTamed(string companionName, CompanionMode mode)
        {
            var definition = Creature.Definition;

            feedingsDone = definition != null ? definition.FeedingsToTame : feedingsDone;
            Creature.IsTamed = true;
            Creature.OwnerSpawner?.ReleaseOwnership(Creature);
            ApplyTamedModifiers();

            CompanionName = string.IsNullOrWhiteSpace(companionName)
                ? (definition != null ? definition.DisplayName : "Companion")
                : companionName;
            gameObject.name = $"Companion_{(definition != null ? definition.Id : "unknown")}";
            Creature.OnDeath += OnCompanionDeath;
            if (!tamedCompanions.Contains(this))
                tamedCompanions.Add(this);

            ai?.EnterTamedMode(mode);
        }

        /// <summary>Naming UI callback: empty input keeps the species name.</summary>
        public void SetName(string newName)
        {
            newName = newName?.Trim();
            if (!string.IsNullOrEmpty(newName))
                CompanionName = newName;

            CompanionUIView.Instance?.ShowMessage($"{CompanionName} joins you.", 3f);
        }

        private void CycleMode()
        {
            CompanionMode next = NextMode();
            ai?.EnterTamedMode(next);
            CompanionUIView.Instance?.ShowMessage($"{CompanionName} will now {DescribeMode(next)}.", 2.5f);
        }

        private CompanionMode NextMode()
        {
            var definition = Creature.Definition;
            switch (Mode)
            {
                case CompanionMode.Follow:
                    return CompanionMode.Stay;
                case CompanionMode.Stay:
                    return definition != null && definition.CanAssistInCombat
                        ? CompanionMode.Assist
                        : CompanionMode.Follow;
                default:
                    return CompanionMode.Follow;
            }
        }

        private static string DescribeMode(CompanionMode mode)
        {
            switch (mode)
            {
                case CompanionMode.Stay: return "stay here";
                case CompanionMode.Assist: return "fight at your side";
                default: return "follow you";
            }
        }

        private void ApplyTamedModifiers()
        {
            var definition = Creature.Definition;
            if (definition == null)
                return;

            var modifiers = definition.TamedStatModifiers;
            for (int i = 0; i < modifiers.Count; i++)
            {
                EquipStatModifier entry = modifiers[i];
                if (string.IsNullOrEmpty(entry.statId))
                    continue;

                Creature.Stats.AddModifier(entry.statId,
                    new StatModifier(this, entry.target, entry.type, entry.value));
            }
        }

        // ------------------------------------------------------------------
        // Death (a companion is mourned, not recycled)
        // ------------------------------------------------------------------

        private void OnCompanionDeath(Creature dead)
        {
            Creature.OnDeath -= OnCompanionDeath;
            tamedCompanions.Remove(this); // saves after this moment drop it

            Debug.Log($"[Taming] Companion '{CompanionName}' died.", this);
            CompanionUIView.Instance?.ShowMessage($"{CompanionName} has died.", 5f);

            // OwnerSpawner was released at tame time, so the body Destroys
            // after the death linger instead of pooling — and no spawner
            // counts it, so nothing ever "respawns" it.
        }

        // ------------------------------------------------------------------
        // Player lookups (shared static cache, the CreatureAI pattern)
        // ------------------------------------------------------------------

        private static bool PlayerHoldsFavoriteFood(Data.Creatures.CreatureDefinition definition)
        {
            if (cachedPlayer == null)
            {
                cachedPlayer = FindFirstObjectByType<PlayerReferences>();
                cachedSelector = cachedPlayer != null ? cachedPlayer.GetComponent<HotbarSelector>() : null;
            }

            return cachedSelector != null && definition.IsFavoriteFood(cachedSelector.EquippedItem);
        }

        private static string FavoriteFoodNames(Data.Creatures.CreatureDefinition definition)
        {
            var foods = definition.FavoriteFoods;
            if (foods.Count == 0)
                return "food";

            if (foods.Count == 1 || foods[1] == null)
                return foods[0] != null ? foods[0].DisplayName : "food";

            return $"{foods[0].DisplayName}/{foods[1].DisplayName}";
        }
    }
}
