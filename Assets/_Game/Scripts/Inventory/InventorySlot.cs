using System;
using IslandGame.Data.Items;
using UnityEngine;

namespace IslandGame.Inventory
{
    /// <summary>
    /// One inventory slot: an ItemDefinition reference, a stack count, and the
    /// per-instance durability (reserved since the inventory phase, live since
    /// the durability phase — this field IS the single runtime home of an
    /// item's condition, and the field save/load serializes). Mutated only by
    /// InventorySystem; everything else reads.
    /// </summary>
    [Serializable]
    public sealed class InventorySlot
    {
        [SerializeField] private ItemDefinition item;
        [SerializeField] private int count;
        [SerializeField] private float durability01 = 1f;

        public ItemDefinition Item => item;
        public int Count => count;

        /// <summary>0-1 condition of the stack. Meaningful for unstackables (tools/weapons); 1 for everything else.</summary>
        public float Durability01 => durability01;

        /// <summary>Durability write, used by InventorySystem's wear/repair paths only.</summary>
        internal void SetDurability01(float value)
        {
            durability01 = Mathf.Clamp01(value);
        }

        public bool IsEmpty => item == null || count <= 0;

        /// <summary>How many more units of the current item fit in this slot. 0 when empty (no item to stack).</summary>
        public int SpaceLeft => IsEmpty ? 0 : Mathf.Max(0, item.MaxStackSize - count);

        public float TotalWeightKg => IsEmpty ? 0f : item.WeightKg * count;

        internal void Set(ItemDefinition newItem, int newCount, float newDurability01)
        {
            item = newItem;
            count = newCount;
            durability01 = Mathf.Clamp01(newDurability01);
            if (IsEmpty)
                Clear();
        }

        internal void AddCount(int amount)
        {
            count += amount;
            if (count <= 0)
                Clear();
        }

        internal void Clear()
        {
            item = null;
            count = 0;
            durability01 = 1f;
        }
    }
}
