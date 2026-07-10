using System;
using System.Collections.Generic;
using IslandGame.Data.Items;
using IslandGame.Player;
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
        [Tooltip("Total kilograms that count as a full load (CarryWeight01 = 1). The movement system's speed/sprint/prone penalties are tuned there, not here.")]
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

        /// <summary>Raised after any inventory mutation (single event; the small grid redraws whole).</summary>
        public event Action InventoryChanged;

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

        public float MaxCarryWeightKg => maxCarryWeightKg;

        private void Awake()
        {
            references = GetComponent<PlayerReferences>();
            EnsureSlots();
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
        /// Adds up to count units, merging into existing stacks first, then
        /// filling empty slots (hotbar first). Returns how many units were
        /// actually stored — callers keep the remainder in the world.
        /// </summary>
        public int AddItem(ItemDefinition item, int count)
        {
            int added = AddItemInternal(item, count);
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
        // Internals
        // ------------------------------------------------------------------

        private int AddItemInternal(ItemDefinition item, int count)
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
                slot.Set(item, moved, 1f);
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
                references.Locomotion.SetCarryWeight(CarryLoad.ToNormalized(TotalWeightKg, maxCarryWeightKg));

            InventoryChanged?.Invoke();
        }
    }
}
