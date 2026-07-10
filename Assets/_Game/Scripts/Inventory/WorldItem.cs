using IslandGame.Data.Items;
using UnityEngine;

namespace IslandGame.Inventory
{
    /// <summary>
    /// An item stack lying in the world — dropped by the player or (later)
    /// spawned by mined blocks and loot. Physical: a Rigidbody plus whatever
    /// colliders the world model carries (a fitted box is added when it has
    /// none, or a placeholder cube when there's no model at all).
    ///
    /// Pickup is polled by the player's ItemPickupCollector (no trigger events —
    /// those go quiet when rigidbodies fall asleep). A short delay after
    /// spawning stops dropped items from being vacuumed straight back up.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldItem : MonoBehaviour
    {
        private const float PickupDelaySeconds = 0.75f;

        public ItemDefinition Item { get; private set; }
        public int Count { get; private set; }
        public float Durability01 { get; private set; } = 1f;

        private float spawnTime;

        public bool CanBePickedUp => Item != null && Count > 0 && Time.time - spawnTime >= PickupDelaySeconds;

        /// <summary>
        /// Moves as much of this stack as fits into the inventory. Returns the
        /// amount taken; the world object survives holding the remainder when
        /// the inventory is full — that IS the "inventory full" feedback.
        /// </summary>
        public int TakeInto(InventorySystem inventory)
        {
            if (inventory == null || Item == null || Count <= 0)
                return 0;

            int added = inventory.AddItem(Item, Count);
            if (added <= 0)
                return 0;

            Count -= added;
            name = BuildName(Item, Count);
            if (Count <= 0)
                Destroy(gameObject);

            return added;
        }

        /// <summary>Factory for dropped/spawned stacks: builds the physical world object at position with an initial velocity.</summary>
        public static WorldItem Spawn(
            ItemDefinition item, int count, float durability01, Vector3 position, Vector3 velocity)
        {
            if (item == null || count <= 0)
                return null;

            var root = new GameObject(BuildName(item, count));
            root.transform.position = position;

            var worldItem = root.AddComponent<WorldItem>();
            worldItem.Item = item;
            worldItem.Count = count;
            worldItem.Durability01 = Mathf.Clamp01(durability01);
            worldItem.spawnTime = Time.time;

            if (item.WorldModelPrefab != null)
            {
                GameObject model = Instantiate(item.WorldModelPrefab, root.transform);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;

                if (root.GetComponentInChildren<Collider>() == null)
                    AddFittedBoxCollider(root);
            }
            else
            {
                // No model authored yet: a small cube keeps the item visible,
                // physical and testable instead of invisible-and-lost.
                GameObject placeholder = GameObject.CreatePrimitive(PrimitiveType.Cube);
                placeholder.name = "Placeholder";
                placeholder.transform.SetParent(root.transform, false);
                placeholder.transform.localScale = Vector3.one * 0.25f;
            }

            var body = root.AddComponent<Rigidbody>();
            body.mass = Mathf.Clamp(item.WeightKg, 0.1f, 50f);
            body.linearVelocity = velocity;
            body.angularVelocity = Random.insideUnitSphere * 2f;

            return worldItem;
        }

        private static void AddFittedBoxCollider(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                var fallback = root.AddComponent<BoxCollider>();
                fallback.size = Vector3.one * 0.25f;
                return;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            var box = root.AddComponent<BoxCollider>();
            box.center = root.transform.InverseTransformPoint(bounds.center);
            box.size = Vector3.Max(bounds.size, Vector3.one * 0.05f);
        }

        private static string BuildName(ItemDefinition item, int count)
        {
            return $"WorldItem_{item.Id} x{count}";
        }
    }
}
