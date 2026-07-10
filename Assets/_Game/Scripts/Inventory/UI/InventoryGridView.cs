using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace IslandGame.Inventory.UI
{
    /// <summary>
    /// The toggleable backpack panel: instantiates one slot cell per backpack
    /// slot (indices HotbarSize..SlotCount-1) from the builder's template and
    /// keeps them plus the weight readout refreshed. Lives on the panel that
    /// gets SetActive-toggled, so cells build lazily on first open and events
    /// are only subscribed while visible.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InventoryGridView : MonoBehaviour
    {
        [Header("Wired by the UI builder")]
        [SerializeField] private InventoryUIController controller;
        [SerializeField] private GameObject slotTemplate;
        [SerializeField] private Text weightLabel;

        private readonly List<InventorySlotView> cells = new List<InventorySlotView>();
        private bool built;

        private void OnEnable()
        {
            EnsureBuilt();
            if (!built)
                return; // wiring/init not ready — the next enable retries

            RefreshAll();
            controller.Inventory.InventoryChanged += RefreshAll;
        }

        private void OnDisable()
        {
            if (controller != null && controller.Inventory != null)
                controller.Inventory.InventoryChanged -= RefreshAll;
        }

        private void EnsureBuilt()
        {
            if (built)
                return;

            // The panel is active in the edit-time scene, so the first OnEnable
            // fires during scene load — possibly before other components exist.
            // Skip WITHOUT latching `built`, so the first real open retries;
            // latching before building is how a thrown exception used to leave
            // the grid permanently empty.
            if (controller == null || controller.Inventory == null)
                return;

            InventorySystem inventory = controller.Inventory;
            for (int i = inventory.HotbarSize; i < inventory.SlotCount; i++)
            {
                GameObject cell = Instantiate(slotTemplate, slotTemplate.transform.parent);
                cell.name = $"BackpackSlot_{i}";
                cell.SetActive(true);

                var view = cell.GetComponent<InventorySlotView>();
                view.Bind(controller, i);
                cells.Add(view);
            }

            slotTemplate.SetActive(false);
            built = true; // only after everything above succeeded
        }

        private void RefreshAll()
        {
            for (int i = 0; i < cells.Count; i++)
                cells[i].Refresh();

            if (weightLabel != null)
            {
                InventorySystem inventory = controller.Inventory;
                weightLabel.text = $"{inventory.TotalWeightKg:0.0} / {inventory.MaxCarryWeightKg:0.0} kg";
            }
        }
    }
}
