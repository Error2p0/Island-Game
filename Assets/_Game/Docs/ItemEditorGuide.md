# Item Editor — Usage Guide (Phase 3)

Open with **Island Game → Items → Item Editor**. Same layout and editing model as the
Block Editor: searchable list left; live preview + inspector + Save/Revert/Delete row
right; all edits live in an unapplied buffer until **Save** (auto-saved before
recompile/play mode; Save/Discard/Cancel prompts guard switching and closing). New
items are created in `Assets/_Game/Content/Items`.

The preview renders the item's **World Model Prefab** (drag to rotate, Auto-Rotate /
Reset View in its toolbar). The **Hold Offset** toolbar toggle (enabled when Hold Type
isn't None) redraws the model under RGB socket axes using the authored local
position/rotation — exactly the transform the held-item phase will apply.

## Creating a weapon (e.g. Club)

1. **New Item** → set Display Name "Club", check the ID.
2. General: icon, category **Weapon**, Max Stack Size 1, weight, World Model Prefab.
3. Toggle **Is Weapon** on: damage 25, Damage Type **Blunt**, Attacks Per Second 1.2,
   Attack Range 2, and drop a swing AnimationClip into Attack Clip (reference only —
   wiring happens in the held-item phase; validation reminds you if it's empty).
4. Holding: Hold Type **OneHanded**, Hold Socket **RightHand**, then tune Hold Local
   Position/Rotation with the Hold Offset preview on until the grip sits on the axes
   origin (the palm).
5. **Save**.

## Creating a tool (e.g. Stone Pickaxe)

1. New Item → "Stone Pickaxe", category **Tool**, stack 1, world model.
2. Toggle **Is Tool** on: Tool Type **Pickaxe**, Tool Tier 2, Mining Speed
   Multiplier 4.
3. In **Efficient Blocks**, search and tick the blocks this pickaxe is fast against
   (e.g. Stone). Remember the split: the tier decides what it *can* mine at all; this
   list only grants the speed multiplier. A Wood Pickaxe would be the same setup with
   Tool Tier 1 and a lower multiplier.
4. Holding: TwoHanded or OneHanded + offsets, as above. Save.

## Creating an item that is both (e.g. Axe)

Toggle **Is Weapon** AND **Is Tool** on — the sections are independent and coexist:
weapon fields (Slash damage, rate, range, clip) drive combat; tool fields (Tool Type
Axe, tier, Efficient Blocks = wood blocks) drive mining. One Holding section serves
both. Validation only complains if a weapon/tool has Hold Type None (it could never be
equipped).

## Validation section

Live at the bottom of the inspector: empty/duplicate IDs (error), weapon/tool with
Hold Type None (warning), Block/Placeable category without a Placed Block (warning),
Placed Block set on a non-placeable category (warning), one-way item↔block links and
held-but-not-weapon/tool (info — legal, just confirming intent).

## Troubleshooting

- "ItemDatabase asset not found" → **Sync Databases Now**.
- Preview says "No world model" → assign a prefab with MeshRenderers or
  SkinnedMeshRenderers.
- Efficient Blocks list disabled → BlockDatabase missing; sync databases.
