using System;
using IslandGame.Data.Items;
using UnityEngine;

namespace IslandGame.Inventory
{
    /// <summary>
    /// One inventory slot: an ItemDefinition reference, a stack count, and the
    /// per-instance durability hook (unused until the durability phase, present
    /// now so slots never need restructuring — restructuring would break every
    /// save). Mutated only by InventorySystem; everything else reads.
    /// </summary>
    [Serializable]
    public sealed class InventorySlot
    {
        [SerializeField] private ItemDefinition item;
        [SerializeField] private int count;
        [SerializeField] private float durability01 = 1f;

        public ItemDefinition Item => item;
        public int Count => count;

        /// <summary>0-1 condition of the stack. Meaningful for unstackables (tools/weapons); reserved for the durability phase.</summary>
        public float Durability01 => durability01;

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
