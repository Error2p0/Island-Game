# Crafting — Recipe Editor & In-Game Menu (Phase 8)

## Setup

1. **Island Game → Data → Create Example Recipes** — creates the Stone Pickaxe tool
   item (tier 1, efficient vs stone, one-handed) and three recipes: Wood Planks ×4 ←
   Log (hand, instant), Compacted Stone ← Sand ×4 (hand, 2 s), Stone Pickaxe ← Stone ×3
   + Plank ×2 (**Workbench**, 2 s).
2. **Island Game → UI → Build Crafting Menu UI** in the gameplay scene (needs the
   inventory UI built first). Save the scene.
3. Workbench stub for testing (station convention, until a real station-object phase):
   empty GameObject + BoxCollider + `CraftingStationMarker` (type Workbench) placed
   near the spawn beach. Any collider carrying that marker within ~3 m of the player
   counts; future placed-station objects attach the same component.

**Toggle key: B** (not C — C is Crouch in the actions asset). Add a `ToggleCrafting`
action to the Player map to rebind properly, same pattern as every other toggle.

## Recipe Editor (Island Game → Crafting → Recipe Editor)

Same list+detail UX as the Block/Item editors: searchable list left (● = unsaved),
buffered Save/Revert editing, auto-save before recompile/play, New Recipe creates in
`Assets/_Game/Content/Recipes`, delete is a plain confirm (nothing references recipes).

Detail panel: Display Name/ID (live collision check), **Output** via searchable
ItemDatabase dropdown or drag-drop + quantity, **ingredient rows** (each with its own
searchable picker + count + ✕ remove; Add Ingredient appends), **Station** dropdown,
**Craft Seconds** (0 = instant). Validation section flags: dangling output/ingredient
references (deleted items — also flagged red per row), no ingredients, duplicate
ingredient rows, output-as-ingredient (info), output count above stack size (info).

## Runtime rules (CraftingSystem)

- **Order of operations**: ingredients present → station near → **output fits** —
  all verified *before anything is consumed*, so a full inventory can never eat
  ingredients. The fit check is deliberately conservative (space freed by the
  consumption itself isn't counted); as a race guard, output that still can't fit
  spawns as a WorldItem — never deleted.
- **Timed crafts consume at completion**, with revalidation: walking away from the
  station or losing ingredients mid-craft aborts with a reason and costs nothing.
- The menu's craftable-only filter checks ingredients only; station and space show
  as explicit reasons on the Craft button instead.

## Test checklist

1. **Author a recipe**: Recipe Editor → New Recipe → output Dirt ×2, ingredient
   Sand ×1, hand, instant → Save. It appears in the in-game menu immediately.
2. **Craft in-game**: gather a Log (or F1-give one), press B → Wood Planks row is
   white (craftable); ingredient line shows 1/1 green; Craft → 4 planks appear,
   log gone, weight readout updates.
3. **Insufficient ingredients**: with no logs, the row greys out; selecting it shows
   "Missing 1 × Log." and a disabled Craft button. Toggle "Craftable only" — it
   disappears from the list.
4. **Station gating**: select Stone Pickaxe with stone+planks in inventory but no
   workbench near — red "Requires Workbench — none nearby", Craft disabled. Walk to
   the marker object — line turns green, Craft enables; the 2 s progress bar fills,
   then the pickaxe lands in the inventory. Walk out of range mid-craft — it aborts,
   nothing consumed.
5. **Output-full case**: fill every slot (F1 full stacks), keep 4+ sand, try
   Compacted Stone — "No room for the output — free up inventory space first.",
   Craft disabled, sand untouched.
6. **Dangling reference**: delete a test item used by your test recipe (note the
   delete warning now lists recipes!) — the Recipe Editor shows the red dangling
   error on the row, and in-game the recipe refuses with the fix-it reason.
