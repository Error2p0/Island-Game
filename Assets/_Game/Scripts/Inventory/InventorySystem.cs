using System;
using System.Collections.Generic;
using IslandGame.Data.Items;
using IslandGame.Data.Stats;
using IslandGame.Player;
using IslandGame.Stats;
using UnityEngine;

namespace IslandGame.Inventory
{
    /// <summary>
    /// The player's inventory: a fixed slot array (hotbar occupies indices
    /// 0..HotbarSize-1, backpack the rest) plus the core operations every other
    /// system uses — pickup (AddItem), crafting later (Remove/GetItemCount/
    /// CanFitItem), the UI (Move/Split/Drop) and the hotbar (GetSlot).
    ///
    /// Pure logic + events; zero UI in here. Every mutation funnels through
    /// NotifyChanged(), which is what keeps requirement #5 honest: total weight
    /// is pushed into PlayerLocomotion.SetCarryWeight (via CarryLoad) on EVERY
    /// change, and InventoryChanged fires for the views.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InventorySystem : MonoBehaviour
    {
        [Header("Capacity")]
        [Tooltip("Slots 0..N-1 are the hotbar (max 9 — that's all the number keys there are).")]
        [Range(1, 9)]
        [SerializeField] private int hotbarSize = 9;

        [Min(0)]
        [SerializeField] private int backpackSize = 27;

        [Header("Carry Weight")]
        [Tooltip("Fallback only: total kilograms that count as a full load (CarryWeight01 = 1) when the player has no StatContainer with a 'carry_capacity' stat. Stat-backed capacity is authored on the CarryCapacity StatDefinition and buffed via modifiers. The movement system's speed/sprint/prone penalties are tuned there, not here.")]
        [Min(1f)]
        [SerializeField] private float maxCarryWeightKg = 60f;

        [Header("Dropping")]
        [Tooltip("How far in front of the camera dropped items appear.")]
        [SerializeField] private float dropDistance = 0.9f;

        [SerializeField] private float dropForwardSpeed = 2.5f;
        [SerializeField] private float dropUpSpeed = 1.2f;

        [Header("Starting Items (testing until world content exists)")]
        [SerializeField] private List<StartingItem> startingItems = new List<StartingItem>();

        [Serializable]
        private struct StartingItem
        {
            public ItemDefinition item;
            public int count;
        }

        private InventorySlot[] slots;
        private PlayerReferences references;
        private StatContainer statContainer;

        /// <summary>Raised after any inventory mutation (single event; the small grid redraws whole).</summary>
        public event Action InventoryChanged;

        /// <summary>Raised when a unit breaks from durability loss, with the item that broke — HUD toasts/SFX later.</summary>
        public event Action<ItemDefinition> ItemBroke;

        public int SlotCount
        {
            get
            {
                EnsureSlots();
                return slots.Length;
            }
        }

        public int HotbarSize => hotbarSize;

        /// <summary>Sum of all slot weights, kilograms. Updated on every change.</summary>
        public float TotalWeightKg { get; private set; }

        /// <summary>
        /// Kilograms that count as a full load. Backed by the carry_capacity
        /// stat when the entity has one (buffable via modifiers from the stats
        /// phase on); the serialized field remains the fallback.
        /// </summary>
        public float MaxCarryWeightKg =>
            statContainer != null && statContainer.Has(StatIds.CarryCapacity)
                ? Mathf.Max(1f, statContainer.GetValue(StatIds.CarryCapacity, maxCarryWeightKg))
                : maxCarryWeightKg;

        private void Awake()
        {
            references = GetComponent<PlayerReferences>();
            statContainer = GetComponent<StatContainer>();
            EnsureSlots();
        }

        private void OnEnable()
        {
            if (statContainer != null)
                statContainer.OnStatChanged += OnStatChanged;
        }

        private void OnDisable()
        {
            if (statContainer != null)
                statContainer.OnStatChanged -= OnStatChanged;
        }

        /// <summary>A carry-capacity buff/debuff changes the normalized load without any item moving — re-push it.</summary>
        private void OnStatChanged(string statId, float oldValue, float newValue)
        {
            if (statId == StatIds.CarryCapacity)
                NotifyChanged();
        }

        /// <summary>
        /// Allocates the slot array on first use. Called from Awake AND from
        /// every public entry point: scene-load Awake/OnEnable order across
        /// different objects is undefined in Unity, so UI views can legally
        /// query the inventory before this component's Awake has run.
        /// </summary>
        private void EnsureSlots()
        {
            if (slots != null)
                return;

            slots = new InventorySlot[hotbarSize + backpackSize];
            for (int i = 0; i < slots.Length; i++)
                slots[i] = new InventorySlot();
        }

        private void Start()
        {
            foreach (StartingItem entry in startingItems)
            {
                if (entry.item != null && entry.count > 0)
                    AddItemInternal(entry.item, entry.count);
            }

            // First real consumer of PlayerLocomotion.SetCarryWeight: push the
            // initial load even when empty, so the stale editor value can't linger.
            NotifyChanged();
        }

        public InventorySlot GetSlot(int index)
        {
            EnsureSlots();
            return slots[index];
        }

        public bool IsHotbarIndex(int index)
        {
            return index >= 0 && index < hotbarSize;
        }

        // ------------------------------------------------------------------
        // Core operations
        // ------------------------------------------------------------------

        /// <summary>
        /// Adds up to count units at full condition, merging into existing
        /// stacks first, then filling empty slots (hotbar first). Returns how
        /// many units were actually stored — callers keep the remainder in the
        /// world.
        /// </summary>
        public int AddItem(ItemDefinition item, int count)
        {
            return AddItem(item, count, 1f);
        }

        /// <summary>
        /// Durability-preserving add (picking a dropped tool back up). The
        /// durability applies to units landing in EMPTY slots; units merged
        /// into existing stacks keep the stack's durability — in practice
        /// durability-bearing items are unstackable (MaxStackSize 1), so a
        /// worn tool always occupies its own slot with its own condition.
        /// </summary>
        public int AddItem(ItemDefinition item, int count, float durability01)
        {
            int added = AddItemInternal(item, count, durability01);
            if (added > 0)
                NotifyChanged();
            return added;
        }

        /// <summary>Removes up to count units of the item (backpack slots first, hotbar last). Returns how many were removed.</summary>
        public int RemoveItem(ItemDefinition item, int count)
        {
            if (item == null || count <= 0)
                return 0;

            EnsureSlots();
            int remaining = count;
            for (int i = slots.Length - 1; i >= 0 && remaining > 0; i--)
            {
                InventorySlot slot = slots[i];
                if (slot.Item != item)
                    continue;

                int taken = Mathf.Min(slot.Count, remaining);
                slot.AddCount(-taken);
                remaining -= taken;
            }

            int removed = count - remaining;
            if (removed > 0)
                NotifyChanged();
            return removed;
        }

        /// <summary>True when the full count would fit (existing stack space + empty slots).</summary>
        public bool CanFitItem(ItemDefinition item, int count)
        {
            if (item == null || count <= 0)
                return false;

            EnsureSlots();
            int remaining = count;
            for (int i = 0; i < slots.Length && remaining > 0; i++)
            {
                InventorySlot slot = slots[i];
                if (slot.IsEmpty)
                    remaining -= item.MaxStackSize;
                else if (slot.Item == item)
                    remaining -= slot.SpaceLeft;
            }

            return remaining <= 0;
        }

        /// <summary>Total units of the item across all slots (crafting cost checks).</summary>
        public int GetItemCount(ItemDefinition item)
        {
            if (item == null)
                return 0;

            EnsureSlots();
            int total = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].Item == item)
                    total += slots[i].Count;
            }

            return total;
        }

        /// <summary>
        /// UI drag-and-drop: onto an empty slot = move, onto the same item with
        /// space = merge (leftover stays in the source), otherwise = swap.
        /// </summary>
        public bool MoveOrMergeSlot(int fromIndex, int toIndex)
        {
            if (!IsValidIndex(fromIndex) || !IsValidIndex(toIndex) || fromIndex == toIndex)
                return false;

            InventorySlot from = slots[fromIndex];
            InventorySlot to = slots[toIndex];
            if (from.IsEmpty)
                return false;

            if (to.IsEmpty)
            {
                to.Set(from.Item, from.Count, from.Durability01);
                from.Clear();
            }
            else if (to.Item == from.Item && to.SpaceLeft > 0)
            {
                int moved = Mathf.Min(to.SpaceLeft, from.Count);
                to.AddCount(moved);
                from.AddCount(-moved);
            }
            else
            {
                ItemDefinition item = to.Item;
                int itemCount = to.Count;
                float durability = to.Durability01;
                to.Set(from.Item, from.Count, from.Durability01);
                from.Set(item, itemCount, durability);
            }

            NotifyChanged();
            return true;
        }

        /// <summary>Moves amount units from a stack into an EMPTY slot, keeping the rest behind.</summary>
        public bool SplitStack(int fromIndex, int toIndex, int amount)
        {
            if (!IsValidIndex(fromIndex) || !IsValidIndex(toIndex) || fromIndex == toIndex)
                return false;

            InventorySlot from = slots[fromIndex];
            InventorySlot to = slots[toIndex];
            if (from.IsEmpty || !to.IsEmpty || amount <= 0 || amount >= from.Count)
                return false;

            to.Set(from.Item, amount, from.Durability01);
            from.AddCount(-amount);
            NotifyChanged();
            return true;
        }

        /// <summary>UI right-click: half the stack into the first empty slot (backpack preferred).</summary>
        public bool SplitStackToFirstEmpty(int fromIndex)
        {
            if (!IsValidIndex(fromIndex) || slots[fromIndex].Count < 2)
                return false;

            int target = FindFirstEmptySlot(fromIndex);
            return target >= 0 && SplitStack(fromIndex, target, slots[fromIndex].Count / 2);
        }

        /// <summary>
        /// Removes up to count units from one SPECIFIC slot (block placement
        /// consuming from the equipped hotbar slot, future consumables).
        /// Returns the amount actually removed.
        /// </summary>
        public int ConsumeFromSlot(int slotIndex, int count)
        {
            if (!IsValidIndex(slotIndex) || count <= 0)
                return 0;

            InventorySlot slot = slots[slotIndex];
            if (slot.IsEmpty)
                return 0;

            int taken = Mathf.Min(slot.Count, count);
            slot.AddCount(-taken);
            NotifyChanged();
            return taken;
        }

        /// <summary>
        /// Removes up to count units from the slot and spawns them as a world
        /// item just in front of the camera, inheriting the stack's durability.
        /// </summary>
        public bool DropFromSlot(int slotIndex, int count)
        {
            if (!IsValidIndex(slotIndex) || count <= 0)
                return false;

            InventorySlot slot = slots[slotIndex];
            if (slot.IsEmpty)
                return false;

            int dropped = Mathf.Min(count, slot.Count);

            Transform origin = references != null && references.CameraPivot != null
                ? references.CameraPivot
                : transform;
            Vector3 position = origin.position + origin.forward * dropDistance;
            Vector3 velocity = origin.forward * dropForwardSpeed + Vector3.up * dropUpSpeed;

            WorldItem.Spawn(slot.Item, dropped, slot.Durability01, position, velocity);

            slot.AddCount(-dropped);
            NotifyChanged();
            return true;
        }

        // ------------------------------------------------------------------
        // Save/load (slot-level state access; content stays ID-referenced)
        // ------------------------------------------------------------------

        /// <summary>
        /// Load-time slot write: sets one slot silently (no notification —
        /// call NotifyExternalRestore once after the loop). Null item clears
        /// the slot. Out-of-range indices (save from a differently-sized
        /// inventory) are ignored with a warning.
        /// </summary>
        public void RestoreSlot(int index, ItemDefinition item, int count, float durability01)
        {
            EnsureSlots();
            if (index < 0 || index >= slots.Length)
            {
                Debug.LogWarning($"[InventorySystem] Restored slot index {index} out of range ({slots.Length} slots) — skipped.", this);
                return;
            }

            if (item == null || count <= 0)
                slots[index].Clear();
            else
                slots[index].Set(item, Mathf.Min(count, item.MaxStackSize), durability01);
        }

        /// <summary>One change notification after a batch of RestoreSlot calls — weight, views and equip state all refresh.</summary>
        public void NotifyExternalRestore()
        {
            NotifyChanged();
        }

        // ------------------------------------------------------------------
        // Durability (the reserved per-slot field, live since this phase)
        // ------------------------------------------------------------------

        /// <summary>
        /// Applies wear to the slot's item, in the item's own durability
        /// POINTS (converted to the slot's 0-1 field via MaxDurability).
        /// Reaching zero triggers the item's authored break behavior
        /// immediately — a broken unit never lingers at 0 in the inventory.
        /// No-op for items without durability.
        /// </summary>
        public void ApplyDurabilityDamage(int slotIndex, float durabilityPoints)
        {
            if (!IsValidIndex(slotIndex) || durabilityPoints <= 0f)
                return;

            InventorySlot slot = slots[slotIndex];
            if (slot.IsEmpty || !slot.Item.HasDurability)
                return;

            float newDurability01 = slot.Durability01 - durabilityPoints / slot.Item.MaxDurability;
            if (newDurability01 > 0f)
            {
                slot.SetDurability01(newDurability01);
            }
            else
            {
                HandleBreak(slot);
            }

            NotifyChanged();
        }

        /// <summary>Sets a slot's condition directly (workbench repair now, save/load in the persistence phase).</summary>
        public void SetSlotDurability01(int slotIndex, float durability01)
        {
            if (!IsValidIndex(slotIndex))
                return;

            InventorySlot slot = slots[slotIndex];
            if (slot.IsEmpty || Mathf.Approximately(slot.Durability01, durability01))
                return;

            slot.SetDurability01(durability01);
            NotifyChanged();
        }

        /// <summary>
        /// Zero-durability outcome, both authored behaviors fully handled:
        /// Destroy removes the unit; Downgrade swaps it for the Broken Variant
        /// at that variant's full condition. Stacks of durability items are a
        /// degenerate case (they're MaxStackSize 1 by convention) but handled:
        /// one unit breaks, the rest of the stack is fresh.
        /// </summary>
        private void HandleBreak(InventorySlot slot)
        {
            ItemDefinition item = slot.Item;

            bool downgrade = item.BreakBehavior == ItemBreakBehavior.DowngradeToBrokenVariant;
            if (downgrade && item.BrokenVariant == null)
            {
                Debug.LogWarning(
                    $"[InventorySystem] '{item.DisplayName}' is set to downgrade on break but has no Broken Variant " +
                    "authored — destroying it instead. Fix the item in the Item Editor.", this);
                downgrade = false;
            }

            if (slot.Count <= 1)
            {
                if (downgrade)
                    slot.Set(item.BrokenVariant, 1, 1f);
                else
                    slot.Clear();
            }
            else
            {
                slot.AddCount(-1);
                slot.SetDurability01(1f); // the next unit of the stack is fresh

                if (downgrade)
                {
                    int stored = AddItemInternal(item.BrokenVariant, 1);
                    if (stored == 0)
                    {
                        WorldItem.Spawn(item.BrokenVariant, 1, 1f,
                            transform.position + Vector3.up * 1.2f, Vector3.up);
                    }
                }
            }

            Debug.Log(downgrade
                ? $"[InventorySystem] {item.DisplayName} broke → {item.BrokenVariant.DisplayName}."
                : $"[InventorySystem] {item.DisplayName} broke and was destroyed.");
            ItemBroke?.Invoke(item);
        }

        // ------------------------------------------------------------------
        // Internals
        // ------------------------------------------------------------------

        private int AddItemInternal(ItemDefinition item, int count, float durability01 = 1f)
        {
            if (item == null || count <= 0)
                return 0;

            EnsureSlots();
            int remaining = count;

            for (int i = 0; i < slots.Length && remaining > 0; i++)
            {
                InventorySlot slot = slots[i];
                if (slot.Item != item || slot.SpaceLeft <= 0)
                    continue;

                int moved = Mathf.Min(slot.SpaceLeft, remaining);
                slot.AddCount(moved);
                remaining -= moved;
            }

            for (int i = 0; i < slots.Length && remaining > 0; i++)
            {
                InventorySlot slot = slots[i];
                if (!slot.IsEmpty)
                    continue;

                int moved = Mathf.Min(item.MaxStackSize, remaining);
                slot.Set(item, moved, durability01);
                remaining -= moved;
            }

            return count - remaining;
        }

        private int FindFirstEmptySlot(int excludeIndex)
        {
            // Backpack first so splits don't silently eat hotbar slots.
            for (int i = hotbarSize; i < slots.Length; i++)
            {
                if (i != excludeIndex && slots[i].IsEmpty)
                    return i;
            }

            for (int i = 0; i < hotbarSize; i++)
            {
                if (i != excludeIndex && slots[i].IsEmpty)
                    return i;
            }

            return -1;
        }

        private bool IsValidIndex(int index)
        {
            EnsureSlots();
            return index >= 0 && index < slots.Length;
        }

        private void NotifyChanged()
        {
            EnsureSlots();
            TotalWeightKg = 0f;
            for (int i = 0; i < slots.Length; i++)
                TotalWeightKg += slots[i].TotalWeightKg;

            if (references != null && references.Locomotion != null)
                references.Locomotion.SetCarryWeight(CarryLoad.ToNormalized(TotalWeightKg, MaxCarryWeightKg));

            InventoryChanged?.Invoke();
        }
    }
}
