# Island Game — Data Conventions (Phase 1)

Reference for every later phase (editor tools, inventory, voxel terrain, crafting,
held items). If a later phase seems to need a different convention, change this doc
and the code together — never quietly diverge.

## Layout

| What | Where |
|---|---|
| Runtime data layer | `Assets/_Game/Scripts/Data/` (`IslandGame.Data`, `.Items`, `.Blocks`) |
| Editor data tooling | `Assets/_Game/Scripts/Editor/Data/` (`IslandGame.EditorTools.Data`) |
| Authored content assets | `Assets/_Game/Content/` (Items, Blocks, Textures) — can live anywhere, this is the convention |
| Database registry assets | `Assets/_Game/Resources/Databases/` — fixed path, runtime loads from here |

## Stable IDs

- Every definition (`ItemDefinition`, `BlockDefinition`, later `RecipeDefinition`)
  has a `string Id`, lowercase_underscore (e.g. `wood_plank`).
- The ID is the **only** thing other systems serialize: terrain saves store block
  IDs, inventory saves store item IDs, recipes store item IDs. Never an asset
  reference in save data, never an array index.
- IDs are unique **per definition type** (item `stone` and block `stone` coexist —
  separate databases).
- A new asset auto-fills its ID from its asset name (`OnValidate`); after that the
  ID is never regenerated. Renaming the asset file is safe; **changing the ID of
  shipped/saved content is not — treat IDs as immutable once used.**

## Registries (ItemDatabase / BlockDatabase)

- One ScriptableObject database asset per definition type at
  `Resources/Databases/{Item,Block}Database.asset`, accessed at runtime via
  `ItemDatabase.Instance` / `BlockDatabase.Instance` (`Resources.Load`).
- **Why this approach**: plain `Resources.LoadAll` would force every definition
  asset into a Resources folder; Addressables adds a package dependency we don't
  need yet. With a synced database asset, definitions can live anywhere in the
  project, runtime gets one tiny deterministic load, and the explicit list gives
  the editor tools and validation a single authoritative enumeration.
- The lists are populated by **Island Game → Data → Sync Databases**, which also
  runs automatically whenever a definition asset is imported/deleted/moved
  (AssetPostprocessor). Never edit the database lists by hand.
- Duplicate or empty IDs are reported as console errors during sync (with both
  asset names) and again at runtime lookup build; lookups resolve to the first
  asset so the game stays playable while you fix the data.
- Lookup API: `TryGet(id, out def)` when a miss is expected, `Get(id)` when the ID
  comes from save data and should always resolve (logs a clear error on miss).

## Block texture atlas

- Blocks reference **plain individual `Texture2D` assets** (uniform or per-face
  with fallback — see `BlockDefinition.GetFaceTexture`). Atlas layout is **never
  authored or serialized**.
- `BlockTextureAtlas.Build(BlockDatabase.Instance.All)` packs all referenced
  textures at runtime (world load); the terrain mesher (Phase 6) calls
  `GetUVRect(block, face)` per emitted face. Because rects live only in memory,
  adding/removing block textures can never corrupt saved worlds.
- Source texture import requirements: **Read/Write enabled**, square, ideally one
  common size (16/32/64 px), no mipmaps needed; the atlas is point-filtered.
  Violations show the magenta/black error tile in-world and log the asset name.
- Face convention: `Top=+Y, Bottom=−Y, North=+Z, South=−Z, East=+X, West=−X`
  (`BlockFace`, iterate with `BlockFaces.All`).
- Editor check without terrain: **Island Game → Data → Build Block Atlas Preview**
  logs all UV rects and writes the packed PNG to `Content/Debug`.

## Item weight → movement

- `ItemDefinition.WeightKg` is real kilograms per unit.
- `PlayerLocomotion` remains the single owner of how load feels (normalized 0–1
  via `SetCarryWeight`). The inventory phase sums kilograms and converts with
  `CarryLoad.ToNormalized(totalKg, capacityKg)`; `capacityKg` will be authored on
  the inventory settings asset.

## Tool mining model (Phase 3)

Two orthogonal concepts — do not merge them:

- **Permission** = tier check: a tool may mine a block iff
  `ItemDefinition.ToolTier >= BlockDefinition.RequiredToolTier`. Scales
  automatically — a new tier-2 block is instantly minable by every tier≥2 tool,
  no tool asset needs touching.
- **Efficiency** = curated list: `ItemDefinition.EfficientBlocks` names the
  blocks this tool mines at its `MiningSpeedMultiplier` (axe → logs/planks,
  pickaxe → stone/ores). Blocks within tier but off the list mine at bare-hand
  speed (1×). Query with `item.IsEfficientAgainst(block)`.

`ToolType` (Axe/Pickaxe/Shovel/Hoe) is descriptive metadata for UI/filtering and
future rules; it grants nothing by itself.

## Hold socket convention (Phase 3 authors it, the held-item phase implements it)

Defined in code on `HoldSocketConvention` (Data/Items/ItemHolding.cs):

- The upper-body rig gets one child transform per `HoldSocket` value, parented
  to the matching hand bone: `Socket_RightHand`, `Socket_LeftHand`
  (`HoldSocketConvention.GetSocketTransformName`). Socket transforms are aligned
  to the palm once; **per-item fit never lives on the socket.**
- Equipping instantiates `ItemDefinition.WorldModelPrefab` as a child of that
  socket with `localPosition = HoldLocalPosition`,
  `localRotation = HoldLocalRotation` (euler field: `HoldLocalRotationEuler`),
  `localScale = one`.
- The Item Editor's "Hold Offset" preview renders exactly this transform against
  RGB socket axes, so numbers authored there transfer 1:1 to the rig.
- `HoldType` picks the upper-body Animator pose (None = not holdable;
  OffHand = secondary-hand items like shields/torches).

## Extending data enums

`ItemCategory`, `HoldType`, `HoldSocket`, `DamageType`, `ToolType`, `BlockFace`,
`BlockBehaviorFlags`: assets serialize numeric values — **append with the next
unused value/bit, never reorder, renumber or delete entries.**

## Building pieces & snap sockets (Building Phase 1)

- Same stable-ID + ScriptableObject + database pattern: `BuildingPieceDefinition`
  (`IslandGame.Data.Building`), registered in
  `Resources/Databases/BuildingPieceDatabase.asset` by the same sync. Authored
  content convention: `Assets/_Game/Content/Building/{Pieces,Prefabs,Materials}`.
- **Prefab contract**: the placed prefab's ROOT carries a `BuildingPiece`
  component (`IslandGame.Building`) with the definition's ID stamped into its
  `pieceId` (the Building Piece Editor validates and can stamp it); every
  visible child needs a collider. Weapon hits resolve `IDamageable` via
  `GetComponentInParent`, so children never need components.
- **Functional placeables** (campfire, workbench, ...) implement
  `IFunctionalPlaceable` on the prefab: `Init(BuildingPiece)` runs exactly once
  when the piece enters the world, `Interact(GameObject)` on player use.
  Station recipes still use the separate `CraftingStationMarker` — orthogonal.
- **Build grid**: 2 m. Foundations 2×0.5×2 (top at local y = 0.5), walls/door
  frames 2 w × 2 h, floors 2×2, roof pieces 2×2 footprint at 45° (rise 2 m).
  All prefab pivots at bottom-center.
- **Snap sockets** live on the DEFINITION (not as prefab children): named
  local-space frames with position + rotation. A socket's **+Z points OUT of
  the piece** (where the neighbor attaches), +Y up. Mating (Phase 2 contract,
  implemented by `SnapSocket.SolveMatedPose`): ghost socket position ==
  target socket position, ghost socket rotation == target socket rotation
  flipped 180° about the target's local up — forwards oppose, ups align.
- **Tagging**: `Tag` = what the socket IS; `AcceptedTags` = what may snap onto
  it. Two sockets can mate when EITHER side accepts the other's tag
  (`SnapSocket.CanMate`) — one-sided authoring suffices; both-sided (as in the
  example content) is harmless self-documentation. Tags are stable serialized
  lowercase_underscore strings (never rename shipped ones); the standard
  vocabulary lives in `SnapTags`: `foundation_top` accepts walls/floors,
  `foundation_side` tiles foundations, `wall_bottom`/`wall_top`/`wall_side`,
  `floor_edge`, `roof_bottom`/`roof_top`/`roof_side`, `doorway`/`door_hinge`.
- `MaterialCost` on the definition is a typed placeholder (item + count, same
  shape as `RecipeIngredient`): Phase 3 turns it into a real recipe link; until
  then the placement UI charges it directly.
- `MaxHealth` is authored now; `BuildingPiece` already initializes from it,
  takes `IDamageable` damage and destroys itself at 0 (raising `Destroyed`).
- `BuildingCategory` is descriptive (build-menu grouping, per-kind placement
  rules) — snapping is driven ONLY by socket tags. Append-only enum.

### Placement (Building Phase 2)

- **Selection = the hotbar**: `ItemDefinition.PlacedPiece` (item→piece, the
  mirror of `PlacedBlock`) makes an equipped Placeable item drive
  `BuildingPlacementController`'s ghost. Set Placed Block OR Placed Piece on
  an item, never both — the place button (right mouse) is shared and the Item
  Editor flags the conflict.
- **Inputs**: R rotates the ghost in 45° yaw steps (rectilinear 2 m grid +
  diagonals; snapped orientation comes from sockets, where R cycles the legal
  poses instead). Middle mouse deconstructs the aimed piece (Valheim's remove
  binding). Both are optional actions ("RotatePiece"/"Deconstruct") with
  keyboard/mouse fallbacks in PlayerInputHandler, same pattern as Mine/Place.
- **Snap selection rule**: among compatible socket pairs within 1.5 m of the
  aim point, nearest target socket wins; ties broken by smallest angle to the
  player's desired yaw. Free placement grid-rounds to 0.5 m XZ / 0.25 m Y.
- **Validity**: the prefab's BoxColliders (renderer-bounds fallback) are
  overlap-tested with a 0.05 m skin. Voxel TERRAIN overlap is LEGAL
  (foundations sink into uneven ground); placed pieces, props and the player
  block placement.
- **Registry**: `PlacedPieceRegistry` (scene object "PlacedPieces",
  auto-created) is the single "what's built where" list — BuildingPiece
  registers on Initialize/Start and unregisters on destroy, placed pieces are
  parented under it. Queries are linear scans; add spatial hashing THERE if
  profiling ever demands it.
- **Deconstruct refund**: Refund Ratio (default 0.5) × MaterialCost, floored,
  min 1 per costed line; overflow drops as WorldItems. Placeholder until the
  Phase 3 recipe link replaces MaterialCost.
- **Aim ray**: all interaction systems share `PlayerAimRaycast` (nearest
  non-trigger hit skipping the player's own rig) — never write another one.
- ~~Placement deliberately does NOT check or consume resources yet~~ — done in
  Building Phase 3, see below.

### Recipes & functional behavior (Building Phase 3)

- **Building recipes**: `RecipeDefinition` has TWO output slots — `Output`
  (item, lands in inventory) and `OutputPiece` (building piece). Set EXACTLY
  one; `IsBuildingRecipe` and `HasDanglingReferences` enforce it, the Recipe
  Editor's Output Type selector authors it. One recipe per piece (placement
  charges the FIRST `RecipeDatabase.FindForPiece` match; the editor warns on
  duplicates). Building recipes ignore `OutputCount`/`CraftSeconds`.
- **Cost flow**: the crafting menu's Building tab "Build" button arms
  `BuildingPlacementController.ArmPiece` and closes the menu. The ghost
  validates the recipe live via `CraftingSystem.ValidateRequirements` (shared
  with hand crafting — never reimplement ingredient checks) and turns red
  when unpayable;
  `CraftingSystem.TryConsumeIngredientsFor` pays at CONFIRM only. Pieces with
  no linked recipe place free. Station requirements apply to building recipes
  too (door frame needs a Workbench nearby to place — Valheim-style).
- **Portable-kit variant** (the flagged alternative, not a special case): a
  normal ITEM recipe whose output item has `PlacedPiece`. Same placement
  controller, same recipe charge on placement.
- **Deconstruct refund** = Refund Ratio × the linked recipe's ingredients
  (fallback: legacy `MaterialCost`, which is now authoring seed data only).
- **Interaction**: E (optional "Interact" action) →
  `PlayerInteraction` raycasts the shared aim ray and calls
  `IFunctionalPlaceable.Interact`; `AimedPrompt` is ready for a future HUD.
- **Campfire** (`CampfireBehavior`): Interact with an accepted fuel item
  equipped feeds 1 unit (and auto-lights); otherwise Interact toggles
  lit/unlit (needs fuel to light). Burns down in real time; point light +
  flame particles while lit. Query `IsLit`/`LitChanged`/`Fuel01` for cooking,
  warmth, visibility. The prefab also carries a Campfire
  `CraftingStationMarker`.
- **Workbench** (`WorkbenchBehavior`): recipe gating is ONLY the existing
  `CraftingStationMarker` (Workbench) on the prefab — no second station
  system; the behavior just opens the crafting menu on Interact.

## Small-voxel subdivision (Voxel Phase 4)

- Big blocks stay the only representation for undamaged terrain — nothing
  about pristine storage, generation or meshing changed. Damaged cells are
  **promoted**: they KEEP their base palette ID in the block array (neighbor
  culling, liquids, gameplay reads unchanged) and gain a `SubVoxelGrid` entry
  in a lazily-allocated per-chunk `Dictionary<flattenedIndex, SubVoxelGrid>`.
  Pristine chunk overhead: zero (`PromotedCount == 0` gates every lookup,
  `HasPromotionsNear` gates the whole mesher pass).
- `SubVoxelGrid` = ulong bitset, resolution³ filled/empty cells (~150 B per
  damaged cell at res 8 vs 2 B pristine). Resolution is configured on
  VoxelWorld (`Sub Voxel Resolution`, default 8) and captured per grid at
  promotion — changing the setting mid-session is safe.
- **API** (all on VoxelWorld): `PromoteBlockToSubVoxels(worldPos)` (lazy
  subdivision — fully filled, visually a no-op, no remesh),
  `GetSubVoxelGrid`, `SetSubVoxel`, and `NotifySubVoxelsChanged(worldPos)`
  after batch grid edits — it demotes the cell to air when the grid empties
  (sparse entry removed) or remeshes chunk + border neighbors. Re-compacting
  an all-full grid back to a plain block is a noted future optimization,
  deliberately not built.
- **Texture continuity**: each face's atlas rect slices into res×res
  sub-rects; a sub-face's (u,v) tile index is the projection of its integer
  sub-coords onto that face's texture axes (`ChunkMesher.SliceFaceUV` — axes
  running negative flip the index). Depth along the normal doesn't enter the
  projection, so carved-open interior faces show the surface's texels.
- **Seams**: promoted cells emit sub-faces instead of base faces; a normal
  block's face against a promoted neighbor is culled ONLY when the touching
  boundary layer is full (`SubVoxelGrid.IsFaceLayerFull`), otherwise drawn
  behind the detail (cheap z-buffered overdraw, never a see-through gap).
  Promoted↔promoted stitches per sub-cell, including across chunk borders.
- **Collision**: sub-faces join the chunk's existing mesh collider (same
  sweep, solid blocks only). Rejected alternatives: per-sub-cell box
  colliders (component explosion), single AABB (can't walk into holes).
- Debug proof: `SubVoxelDebugTester` on the player — F6 corner bite,
  F7 checkerboard, F8 tunnel, F9 clear-all (demotion). Debug keys read the
  keyboard directly by design; they are not PlayerInputHandler actions.
- Save phase note: promotions mark `Chunk.IsModified`; persistence must
  serialize the sparse grids alongside block data.

### Radius mining (Voxel Phase 5)

- `ItemDefinition.MiningRadius` (Tool section, meters): a COMPLETED mining
  hit carves a sub-voxel sphere of this radius at the aim point (biased a
  quarter-radius into the surface). **0 = classic whole-block mining** —
  bare hands and unauthored tools behave exactly as before. Timing, hardness,
  efficiency and the crack overlay are the unchanged pipeline; only the
  geometry outcome differs.
- Carving goes through `VoxelWorld.CarveSphere(center, radius, canCarve,
  fullyEmptied)` — promotes overlapped cells lazily, clears sub-cells whose
  CENTERS lie in the sphere, demotes fully-emptied cells to air, and
  remeshes every touched chunk (+ border neighbors) exactly once per call.
  The permission filter is the caller's: mining skips cells that are above
  the tool's tier, unbreakable, liquid, or non-solid — a sphere never
  bypasses the tier check on neighboring block types.
- **Drop rule (deliberate)**: a cell yields its DropItem only when it FULLY
  empties (reported via `CarvedBlock`); partial carving is visual chip-away.
  Proportional partial drops were rejected: fractional bookkeeping,
  rounding exploits, and total yield should equal classic mining. One bite
  emptying several cells grants each cell's drops. `GrantDrops` in
  PlayerBlockInteraction is the single payout path.
- Debris feedback: `MiningDebrisEffect` (auto-created singleton) bursts
  chip particles tinted by the block's average texture color — not a
  fracture sim, zero per-hit allocation.
- Building placement/deconstruction needs no changes: sub-voxel collision
  lives in the same chunk MeshCollider on the same ChunkView, so
  PlayerAimRaycast and the `GetComponentInParent<ChunkView>()` checks keep
  working on carved terrain.

## Small-voxel trees (Voxel Phase 6)

- Trees are CONTENT on top of the terrain/mining systems: `wood` and `leaves`
  are ordinary BlockDefinitions (leaves transparent-flagged; wood drops the
  `log` item), and chopping is plain Phase 5 radius mining — zero
  tree-specific mining code.
- `TreeTemplateDefinition` (`IslandGame.Data.World`, TreeTemplateDatabase,
  synced like everything): a compact STROKE program — Trunk/Branch capsules
  and Leaves spheres in tree-local meters. Strokes beat raw sub-cell arrays
  (unauthorable, resolution-fragile) and runtime L-systems (determinism,
  inspectability). Content: `Assets/_Game/Content/Trees`.
- **Cost model**: `TreeRasterizer` writes fully-covered cells as PLAIN BASE
  BLOCKS; only boundary cells promote, at the template's own LOW resolution
  (default 4 → 8-byte bitsets, 64-iteration mesh sweeps) — the scale
  mitigation that lets every tree exist fully materialized with no
  distance-promotion machinery. Mixed res-4 tree cells and res-8 mining
  bites coexist via Phase 4's per-grid resolution. One material per cell;
  wood wins where trunk and leaves overlap.
- **Scattering** (`IslandWorldGenerator`, biome layering only — noise/island
  shaping untouched): 8×8-block anchor cells, seeded hashes for occupancy
  (Tree Density), jitter, weighted template pick and yaw; grassy columns
  only (above the beach band). ComputeColumnHeight is a pure function of
  (x,z), so chunks evaluate anchors past their borders and rasterize just
  the local slice — seam-safe, order-independent. Trees generate when chunk
  data first generates, which always precedes player reach — they provably
  predate any building, so no PlacedPieceRegistry check exists.
- **Severing rule (documented choice)**: blocks flagged `NeedsSupport`
  (append-only flag) live only while their connected flagged region touches
  an unflagged solid block. `SupportCollapseSystem` (budgeted, flood-fill
  capped at 1024 cells) reacts to removals; an unsupported region is
  REMOVED AND CONVERTED TO DEBRIS — each cell spawns its DropItem as a
  WorldItem plus chip particles (`VoxelWorld.RemoveCellsAsDebris`, one
  remesh per touched chunk). Physics-falling voxel structures are
  deliberately out of scope (fracture sim). Support is cell-granular: a
  promoted cell counts while any sub-cell remains.
- Leaf texture is opaque on purpose: the transparent submesh alpha-BLENDS
  (water shares it) and holed blended quads sort badly — a dedicated
  alpha-clip leaf material is noted for the visual content pass.
## Day/night cycle (Sky Phase 7)

- `IslandGame.Sky` (Scripts/Sky): `TimeOfDayController` is the ONE clock —
  normalized TimeOfDay01 (0 = midnight, 0.25 = sunrise, 0.5 = noon, 0.75 =
  sunset), configurable Day Length Minutes, `OnSunrise`/`OnDayStart`/
  `OnSunset`/`OnNightStart` events (fire on natural crossings, including
  several in one fast-forwarded frame; `SetTimeOfDay` jumps fire NOTHING by
  design — consumers re-read `IsNight`/`TimeOfDay01`). Future systems
  (spawns, campfire auto-light) subscribe here, never roll their own clock.
- `DayNightVisuals` derives everything visible from sun elevation: sun/moon
  directional lights (exactly ONE shadow caster at a time; moon shadowless),
  gradient skybox colors + celestial disk (doubles as sun and moon), trilight
  ambient, linear fog (closes in at night). All smooth functions — no
  keyframes.
- Sky rendering: custom `IslandGame/SkyGradient` skybox shader (chosen over
  built-in Skybox/Procedural for full scripted control of night palette and
  dusk horizon glow). Stars: `StarField` procedural quad dome (seeded, ~900
  stars, vertex-color brightness/tint) + additive `IslandGame/Stars` shader
  at Queue Transparent-100 — AFTER the skybox (URP draws skyboxes late; a
  Background-queue dome would be overwritten), depth-tested so terrain
  occludes. Dome follows the camera's POSITION only.
- Both material assets (`Content/Sky/{SkyGradient,Stars}.mat`) are
  INSTANTIATED at runtime before per-frame writes — never dirty assets.
- **Playability exposure choice**: night ambient floors at ~8-10% gray-blue
  (never black) + 0.22 moon light — dark and moody but navigable; placed
  light sources (campfire) are what actually carve visibility at night.
- Scene setup: **Island Game → World → Create Day Night Cycle** (also a
  Build Everything step) — builds DayNight/Sun/Moon/Stars, assigns skybox +
  ambient + fog to the scene lighting settings, and DISABLES any other
  directional lights (they would fight the cycle). Debug: F10 pause, hold
  F11 fast-forward, context-menu time jumps on TimeOfDayController.

## Cross-links between items and blocks

- Block → item: `BlockDefinition.DropItem` + `DropCountMin/Max` (what mining yields).
- Item → block: `ItemDefinition.PlacedBlock` (what a Block/Placeable item places).
