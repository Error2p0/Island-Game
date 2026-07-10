using System.Collections.Generic;
using UnityEngine;

namespace IslandGame.Inventory.UI
{
    /// <summary>
    /// Always-visible hotbar strip: instantiates one slot cell per hotbar slot
    /// from the builder's template, keeps them refreshed on inventory changes,
    /// and shows the HotbarSelector's selection highlight.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HotbarView : MonoBehaviour
    {
        [Header("Wired by the UI builder")]
        [SerializeField] private InventoryUIController controller;
        [SerializeField] private HotbarSelector selector;
        [SerializeField] private GameObject slotTemplate;

        private readonly List<InventorySlotView> cells = new List<InventorySlotView>();

        private void Start()
        {
            for (int i = 0; i < controller.Inventory.HotbarSize; i++)
            {
                GameObject cell = Instantiate(slotTemplate, slotTemplate.transform.parent);
                cell.name = $"HotbarSlot_{i + 1}";
                cell.SetActive(true);

                var view = cell.GetComponent<InventorySlotView>();
                view.Bind(controller, i);
                cells.Add(view);
            }

            slotTemplate.SetActive(false);
            RefreshAll();
            OnSelectedSlotChanged(selector.SelectedIndex);

            controller.Inventory.InventoryChanged += RefreshAll;
            selector.SelectedSlotChanged += OnSelectedSlotChanged;
        }

        private void OnDestroy()
        {
            if (controller != null && controller.Inventory != null)
                controller.Inventory.InventoryChanged -= RefreshAll;
            if (selector != null)
                selector.SelectedSlotChanged -= OnSelectedSlotChanged;
        }

        private void RefreshAll()
        {
            for (int i = 0; i < cells.Count; i++)
                cells[i].Refresh();
        }

        private void OnSelectedSlotChanged(int selectedIndex)
        {
            for (int i = 0; i < cells.Count; i++)
                cells[i].SetSelected(i == selectedIndex);
        }
    }
}
