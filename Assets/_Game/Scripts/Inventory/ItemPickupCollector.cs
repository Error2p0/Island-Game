using System.Collections.Generic;
using UnityEngine;

namespace IslandGame.Inventory
{
    /// <summary>
    /// Player-side pickup sweep: every interval, overlap a sphere around the
    /// player and pull eligible WorldItems into the inventory. Polling instead
    /// of trigger events on purpose — trigger callbacks go silent against
    /// sleeping rigidbodies, which is exactly what a dropped item becomes a
    /// second after landing. What doesn't fit stays lying in the world.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(InventorySystem))]
    public sealed class ItemPickupCollector : MonoBehaviour
    {
        [Tooltip("Pickup reach around the player's center, meters.")]
        [SerializeField] private float pickupRadius = 1.6f;

        [Tooltip("Seconds between overlap sweeps — pickup latency vs physics query cost.")]
        [SerializeField] private float sweepInterval = 0.15f;

        [Tooltip("Height of the sweep center above the player root (roughly waist height).")]
        [SerializeField] private float sweepHeight = 0.9f;

        private readonly Collider[] overlapBuffer = new Collider[64];
        private readonly HashSet<WorldItem> sweepSet = new HashSet<WorldItem>();

        private InventorySystem inventory;
        private float nextSweepTime;

        private void Awake()
        {
            inventory = GetComponent<InventorySystem>();
        }

        private void Update()
        {
            if (Time.time < nextSweepTime)
                return;

            nextSweepTime = Time.time + sweepInterval;
            Sweep();
        }

        private void Sweep()
        {
            Vector3 center = transform.position + Vector3.up * sweepHeight;
            int hitCount = Physics.OverlapSphereNonAlloc(
                center, pickupRadius, overlapBuffer, ~0, QueryTriggerInteraction.Collide);

            sweepSet.Clear();
            for (int i = 0; i < hitCount; i++)
            {
                var worldItem = overlapBuffer[i].GetComponentInParent<WorldItem>();
                if (worldItem == null || !worldItem.CanBePickedUp || !sweepSet.Add(worldItem))
                    continue;

                worldItem.TakeInto(inventory);
            }
        }
    }
}
