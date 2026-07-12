using IslandGame.Data.Items;
using IslandGame.Data.Stats;
using IslandGame.Inventory;
using UnityEngine;

namespace IslandGame.Stats
{
    /// <summary>
    /// Bridges the hotbar to the stat system: when the equipped item changes,
    /// the previous item's authored EquipStatModifiers are removed and the new
    /// item's are applied — so a pickaxe with a +mining_speed modifier
    /// genuinely mines faster THROUGH the stat, and future gear (backpacks,
    /// warm clothing) reuses this path untouched. This component is the single
    /// Source for all equip modifiers, which makes cleanup one
    /// RemoveAllFromSource call — no per-modifier bookkeeping.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(StatContainer))]
    [RequireComponent(typeof(HotbarSelector))]
    public sealed class EquippedItemStatModifiers : MonoBehaviour
    {
        private StatContainer statContainer;
        private HotbarSelector selector;

        private void Awake()
        {
            statContainer = GetComponent<StatContainer>();
            selector = GetComponent<HotbarSelector>();
        }

        private void OnEnable()
        {
            selector.EquippedItemChanged += OnEquippedItemChanged;
            OnEquippedItemChanged(selector.EquippedItem);
        }

        private void OnDisable()
        {
            selector.EquippedItemChanged -= OnEquippedItemChanged;
            statContainer.RemoveAllFromSource(this);
        }

        private void OnEquippedItemChanged(ItemDefinition item)
        {
            statContainer.RemoveAllFromSource(this);

            if (item == null)
                return;

            var equipModifiers = item.EquipStatModifiers;
            for (int i = 0; i < equipModifiers.Count; i++)
            {
                EquipStatModifier authored = equipModifiers[i];
                if (string.IsNullOrWhiteSpace(authored.statId))
                    continue;

                bool applied = statContainer.AddModifier(
                    authored.statId,
                    new StatModifier(this, authored.target, authored.type, authored.value));

                if (!applied)
                {
                    Debug.LogWarning(
                        $"[EquippedItemStatModifiers] '{item.DisplayName}' modifies stat '{authored.statId}', " +
                        $"which '{name}' does not have — check the item's Equip Stat Modifiers or the StatContainer's list.",
                        this);
                }
            }
        }
    }
}
