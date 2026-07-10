# Creative Menu — Setup & Usage (Phase 5)

## One-time scene setup

1. Build the inventory UI first (Island Game → UI → Build Inventory UI) if you haven't.
2. Run **Island Game → UI → Build Creative Menu UI** in the gameplay scene. This
   ensures `CreativeModeController` on the player and builds + wires the
   `CreativeMenuCanvas` (panel, search field, tab row, scrollable entry grid, toast).
3. Save the scene.

## Usage

- **F1** toggles the menu (add a `ToggleCreativeMenu` action to the Player input map
  to rebind properly; F1 is the built-in fallback).
- **Tabs**: All / one per ItemCategory / Blocks. Blocks are listed via their item
  form — Drop Item first, else an item whose Placed Block points at the block. A
  block with no item form at all still shows (dimmed); clicking it explains what's
  missing instead of crashing.
- **Search** filters by display name or ID live as you type.
- **Click an entry** to receive it through the normal `InventorySystem.AddItem`
  path. Give Quantity on the CreativeMenuController: 0 = one full stack (default),
  or any fixed count. No stack-limit bypass on purpose: creative gives should
  exercise the real inventory rules — a bypass would let content look fine in
  creative and break in survival. Carry weight applies too (you CAN over-encumber
  yourself with creative gives; that's honest feedback).
- **Toast line** (bottom of panel): "Given 50 × Stone", partial-fit and
  inventory-full messages, and the no-item-form explanation.
- The creative menu and the inventory screen never stack: opening one closes the
  other (shared refcounted `UIInputFocus` keeps cursor/input state correct in any
  order).

## Survival gating

`CreativeModeController` on the player is the master switch. Untick
**Creative Mode Enabled** and the menu refuses to open (force-closes if open when
the flag flips). Nothing needs deleting for survival builds.

## Test checklist

1. Play → **F1**: menu opens, cursor frees, movement/look freeze. F1 again closes.
2. Tabs: "Blocks" shows Stone + Wood Plank (via their drop items); "Resource" shows
   Log; "All" shows everything.
3. Type `sto` in search — list narrows to Stone entries live; clear restores.
4. Click Stone → toast "Given 50 × Stone", hotbar updates. Click repeatedly until
   full → partial toast, then "Inventory full" toast with nothing added.
5. Create a block with no Drop Item in the Block Editor, sync — it appears dimmed
   in Blocks; clicking explains the missing item form. (Delete it after.)
6. Tab while creative is open → creative closes, inventory opens; F1 while
   inventory is open → inventory closes, creative opens. Close everything —
   cursor re-locks, controls return.
7. Untick Creative Mode Enabled on the player (in the inspector during play) —
   the open menu closes itself and F1 does nothing.
