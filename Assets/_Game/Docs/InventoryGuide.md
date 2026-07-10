# Inventory System — Setup & Usage (Phase 4)

## One-time scene setup

1. Open the gameplay scene containing the player (the object with PlayerReferences).
2. Run **Island Game → UI → Build Inventory UI**. This ensures the player has
   `InventorySystem`, `HotbarSelector` and `ItemPickupCollector`, ensures an
   EventSystem with the Input System UI module, builds the `InventoryCanvas`
   (hotbar, backpack panel, tooltip, drag ghost) and wires every reference.
3. Save the scene. Restyle any of the created UI freely — the views only care about
   their wired references.
4. Optional, for testing: on the player's `InventorySystem`, add entries to
   **Starting Items** (e.g. Log ×3, Stone ×20) so you spawn with content.

## Controls

| Input | Action |
|---|---|
| **Tab** or **I** | Toggle backpack panel (unlocks cursor, freezes gameplay input) |
| **1–9** | Select hotbar slot |
| **Scroll wheel** | Cycle hotbar (up = previous, down = next) |
| **G** | Drop one unit of the equipped item |
| Left-drag slot → slot | Move / merge (same item) / swap |
| Left-drag slot → outside the UI | Throw the whole stack into the world |
| Right-click slot | Split half into the first empty slot |
| Hover slot | Tooltip: name, description, per-unit + stack weight, stack fill |

Input bindings: `ToggleInventory` and `DropItem` are read from the actions asset
when those actions exist in the Player map; otherwise PlayerInputHandler falls back
to direct Tab/I and G polling, so nothing needs manual asset editing. Hotbar digits
and scroll are always polled.

## Architecture notes

- `InventorySystem` (player) is pure logic + one `InventoryChanged` event. Every
  mutation funnels through `NotifyChanged()`, which recomputes `TotalWeightKg` and
  pushes `CarryLoad.ToNormalized(total, MaxCarryWeightKg)` into
  `PlayerLocomotion.SetCarryWeight` — the movement system's speed/sprint/prone
  penalties react on *every* change. `Max Carry Weight Kg` is tuned on this component.
- `HotbarSelector` (player) owns the equipped state.
  **`EquippedItemChanged(ItemDefinition)`** is the event the held-item phase (Phase 9)
  subscribes to; it fires on selection change *and* when the selected slot's content
  changes underneath (drop, pickup, drag). `SelectedSlotChanged(int)` is for UI only.
- `WorldItem.Spawn(item, count, durability, position, velocity)` is the single
  factory for ground items — mined-block drops in later phases use the same call.
  Pickup is a polled overlap sweep (`ItemPickupCollector`), not trigger events,
  because sleeping rigidbodies don't fire triggers. If the inventory is full the
  world item simply stays (partial stacks shrink) — that's the "full" feedback.
- Slots carry a `Durability01` field now (unused until the durability phase) so the
  slot layout never has to change.
- UI framework: **uGUI** — the project had no UI yet; chosen for mature
  EventSystem drag-and-drop (pointer handler interfaces), scene-object styling that
  designers can edit directly, and the builder-tool workflow the project already
  uses. Text uses the built-in legacy font to avoid TMP essential-asset imports.

## Test checklist

1. **Add/stack**: with Starting Items set (Stone ×20 twice), enter play — stacks
   merge to max 50 before opening a second slot. Watch the Console stay clean.
2. **Pickup**: drop a stack (G), walk away, walk back over it — it vacuums up after
   its 0.75 s spawn delay. Fill the inventory completely, drop one, pick up something
   else — the leftover stays on the ground.
3. **Move/swap/merge**: Tab → drag Stone onto an empty slot (move), onto Log (swap),
   onto a partial Stone stack (merge, leftover stays).
4. **Split**: right-click a Stone stack — half moves to the first empty backpack slot.
5. **Drop via UI**: drag a stack and release it over the world (outside panels) — it
   spawns in front of the camera with a small toss.
6. **Equip via hotbar**: press 1–9 / scroll — the yellow highlight moves. (Verify the
   event: anything subscribed to `HotbarSelector.EquippedItemChanged` logs; Phase 9
   wires this for real.) Drop the last unit of the equipped stack — the event fires
   with null.
7. **Weight → movement**: note sprint speed unburdened. Set Starting Items to
   Log ×6 (60 kg = full load by default): walk speed drops visibly (55% at full
   load), sprint refuses to engage (above the 0.75 sprint load threshold), and the
   backpack panel's weight readout shows 60/60. Drop logs and feel speed return —
   the weight is pushed on every change.
8. **Input freeze**: with the panel open, WASD/mouse-look/jump do nothing; close —
   control returns and the cursor re-locks.
