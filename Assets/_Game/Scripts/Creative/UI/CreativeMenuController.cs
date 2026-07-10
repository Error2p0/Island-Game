using System;
using System.Collections.Generic;
using IslandGame.Data.Items;
using IslandGame.Inventory;
using IslandGame.Inventory.UI;
using IslandGame.Player;
using UnityEngine;
using UnityEngine.UI;

namespace IslandGame.Creative.UI
{
    /// <summary>
    /// The in-game creative/spawn menu (F1): every item and block from the
    /// databases, filterable by category tabs and live search, click-to-give
    /// through the normal InventorySystem.AddItem path — stack limits, slot
    /// capacity and carry weight all apply exactly as in survival, so creative
    /// testing exercises the real rules (that's the point of the tool; a
    /// bypass toggle would just hide inventory bugs).
    ///
    /// Gated by CreativeModeController: with the flag off the menu refuses to
    /// open and force-closes if the flag goes off while open. Uses the shared
    /// UIInputFocus refcount, and yields to the inventory screen (closes when
    /// Tab is pressed; closes the inventory when it opens).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CreativeMenuController : MonoBehaviour
    {
        [Header("Scene References (wired by the UI builder)")]
        [SerializeField] private PlayerReferences playerReferences;
        [SerializeField] private InventorySystem inventory;
        [SerializeField] private CreativeModeController creativeMode;

        [Tooltip("Optional: closed automatically when this menu opens, so the two screens never stack.")]
        [SerializeField] private InventoryUIController inventoryUI;

        [Header("View References (wired by the UI builder)")]
        [SerializeField] private GameObject panel;
        [SerializeField] private InputField searchField;
        [SerializeField] private GameObject tabTemplate;
        [SerializeField] private GameObject entryTemplate;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private Text toastText;

        [Header("Behavior")]
        [Tooltip("Units handed out per click. 0 = one full stack of the item.")]
        [Min(0)]
        [SerializeField] private int giveQuantity = 0;

        [SerializeField] private float toastDuration = 2.5f;

        public bool IsOpen { get; private set; }

        private readonly List<CreativeTabView> tabViews = new List<CreativeTabView>();
        private readonly List<CreativeEntryView> cellPool = new List<CreativeEntryView>();
        private List<CreativeEntry> catalog;
        private List<(string label, Func<CreativeEntry, bool> filter)> tabs;
        private int selectedTab;
        private string search = string.Empty;
        private float toastHideTime;
        private bool initialized;

        private void Start()
        {
            panel.SetActive(false);
            toastText.text = string.Empty;
        }

        private void OnEnable()
        {
            playerReferences.InputHandler.CreativeMenuTogglePressed += Toggle;
            playerReferences.InputHandler.InventoryTogglePressed += OnInventoryTogglePressed;
        }

        private void OnDisable()
        {
            playerReferences.InputHandler.CreativeMenuTogglePressed -= Toggle;
            playerReferences.InputHandler.InventoryTogglePressed -= OnInventoryTogglePressed;
        }

        private void Update()
        {
            if (!IsOpen)
                return;

            UIInputFocus.EnforceCursor();

            if (!creativeMode.CreativeModeEnabled)
                Close();

            if (toastText.text.Length > 0 && Time.unscaledTime >= toastHideTime)
                toastText.text = string.Empty;
        }

        public void Toggle()
        {
            if (IsOpen)
                Close();
            else
                Open();
        }

        public void Open()
        {
            if (IsOpen || !creativeMode.CreativeModeEnabled)
                return;

            EnsureInitialized();

            // Never stack on top of the inventory screen.
            if (inventoryUI != null && inventoryUI.IsOpen)
                inventoryUI.Toggle();

            IsOpen = true;
            panel.SetActive(true);
            UIInputFocus.Acquire(playerReferences.InputHandler);
            RebuildEntries();
        }

        public void Close()
        {
            if (!IsOpen)
                return;

            IsOpen = false;
            panel.SetActive(false);
            UIInputFocus.Release(playerReferences.InputHandler);
        }

        // ------------------------------------------------------------------
        // Tabs / search / entries
        // ------------------------------------------------------------------

        public void SelectTab(int tabIndex)
        {
            if (tabIndex < 0 || tabIndex >= tabs.Count || tabIndex == selectedTab)
                return;

            selectedTab = tabIndex;
            for (int i = 0; i < tabViews.Count; i++)
                tabViews[i].SetSelected(i == selectedTab);

            RebuildEntries();
        }

        public void GiveEntry(CreativeEntry entry)
        {
            if (entry.GiveItem == null)
            {
                ShowToast($"'{entry.DisplayName}' has no item form yet — give the block a Drop Item (or an item a Placed Block link).");
                return;
            }

            ItemDefinition item = entry.GiveItem;
            int requested = giveQuantity <= 0 ? item.MaxStackSize : giveQuantity;
            int added = inventory.AddItem(item, requested);

            if (added <= 0)
                ShowToast($"Inventory full — no room for {entry.DisplayName}.");
            else if (added < requested)
                ShowToast($"Given {added} × {entry.DisplayName} (inventory full — {requested - added} didn't fit).");
            else
                ShowToast($"Given {added} × {entry.DisplayName}.");
        }

        private void EnsureInitialized()
        {
            if (initialized)
                return;

            initialized = true;
            catalog = CreativeCatalog.Build();
            BuildTabs();
            searchField.onValueChanged.AddListener(OnSearchChanged);
        }

        private void BuildTabs()
        {
            tabs = new List<(string, Func<CreativeEntry, bool>)>
            {
                ("All", _ => true),
            };

            foreach (ItemCategory category in (ItemCategory[])Enum.GetValues(typeof(ItemCategory)))
            {
                ItemCategory captured = category;
                tabs.Add((category.ToString(), entry => !entry.IsBlock && entry.Category == captured));
            }

            tabs.Add(("Blocks", entry => entry.IsBlock));

            for (int i = 0; i < tabs.Count; i++)
            {
                GameObject tab = Instantiate(tabTemplate, tabTemplate.transform.parent);
                tab.name = $"Tab_{tabs[i].label}";
                tab.SetActive(true);

                var view = tab.GetComponent<CreativeTabView>();
                view.Bind(this, i, tabs[i].label);
                view.SetSelected(i == selectedTab);
                tabViews.Add(view);
            }

            tabTemplate.SetActive(false);
        }

        private void OnSearchChanged(string value)
        {
            search = value ?? string.Empty;
            RebuildEntries();
        }

        private void RebuildEntries()
        {
            Func<CreativeEntry, bool> tabFilter = tabs[selectedTab].filter;
            string needle = search.Trim();

            int used = 0;
            for (int i = 0; i < catalog.Count; i++)
            {
                CreativeEntry entry = catalog[i];
                if (!tabFilter(entry) || !MatchesSearch(entry, needle))
                    continue;

                CreativeEntryView cell = GetCell(used++);
                cell.gameObject.SetActive(true);
                cell.Bind(this, entry);
            }

            for (int i = used; i < cellPool.Count; i++)
                cellPool[i].gameObject.SetActive(false);

            scrollRect.verticalNormalizedPosition = 1f;
        }

        private CreativeEntryView GetCell(int index)
        {
            while (index >= cellPool.Count)
            {
                GameObject cell = Instantiate(entryTemplate, entryTemplate.transform.parent);
                cell.name = $"Entry_{cellPool.Count}";
                cellPool.Add(cell.GetComponent<CreativeEntryView>());
            }

            return cellPool[index];
        }

        private static bool MatchesSearch(CreativeEntry entry, string needle)
        {
            if (needle.Length == 0)
                return true;

            return Contains(entry.DisplayName, needle) || Contains(entry.Id, needle);
        }

        private static bool Contains(string text, string needle)
        {
            return !string.IsNullOrEmpty(text)
                   && text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void OnInventoryTogglePressed()
        {
            // Yield to the inventory screen (it opens itself; we just get out
            // of the way — the focus refcount keeps input/cursor state right).
            if (IsOpen)
                Close();
        }

        private void ShowToast(string message)
        {
            toastText.text = message;
            toastHideTime = Time.unscaledTime + toastDuration;
        }
    }
}
