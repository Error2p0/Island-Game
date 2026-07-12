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

## Procedural textures (Texturing Phase 8)

- `IslandGame.Texturing` (Scripts/Texturing) is the pure, RUNTIME-CALLABLE
  core: `TextureSynth.GeneratePixels(style, baseColor, size, seed, hueShift)`
  and `GenerateIconPixels(shape, primary, secondary, size, seed)` —
  deterministic functions (same params + seed = same pixels, forever). Static
  class by design: no per-asset state to serialize, callers own the parameter
  tuples. Each `TextureStyle` (Stone/Wood/Sand/Grass/Metal/Fabric/Foliage/
  Liquid) is a DISTINCT algorithm (mottle+cracks, grain columns+knots,
  speckle+ripples, blade strokes, brushed rows+sheen, weave threads, leaf
  blobs+gaps, depth gradient+glints) — never one noise recolored.
- **Sizes**: block textures default 16×16 (matches all existing content, the
  atlas "one common size" rule, and sub-voxel UV slicing — res-8 sub-faces
  sample 2×2 texel windows); icons 64×64. Both enums are append-only.
- Variation = base color (free) + `TexturePalettes` named presets per style
  (Oak/Birch/Pine, Granite/Basalt/..., the requirement-3 palette-swap list)
  + hue-shift slider + seed.
- Persisting is EDITOR-side: `GeneratedTextureAssets.WriteBlockTexture` /
  `WriteIconSprite` write real .png assets — Point filter, uncompressed, no
  mips; block textures Read/Write enabled (atlas), icons as Sprites.
  OVERWRITE semantics at stable paths (`gen_<id>.png` / `gen_icon_<id>.png`)
  so re-rolling a texture never dangles references — the one deliberate
  exception to creators-never-overwrite, because regeneration is the point.
- Editor UX: "Auto-Generate Texture" in the Block Editor's Textures section
  and "Auto-Generate Icon" in the Item Editor's General section (shared
  `TextureGeneratorSection`): live point-filtered preview on every param
  change; Generate writes the .png immediately but ASSIGNS through the
  session buffer — Save/Revert semantics hold for the definition itself.
- Icons are flat two-tone silhouettes + 1 px outline (`IconShape`:
  RoundedSquare, Circle, Pickaxe, Axe, Sword, Droplet, Ingot; "Match
  Category" suggests one from ItemCategory) — material noise alone doesn't
  read at UI sizes.
- The Phase 9 batch content pass calls `TextureSynth` +
  `GeneratedTextureAssets` directly — same pipeline, no interactive UI.

## Base content set (Content Phase 9)

- **Island Game → Data → Generate Base Content Set** is the reviewable,
  rerunnable batch pass (BaseContentSetGenerator + ContentSetItemsAndBlocks/
  Placeables/Recipes). Contract: DEFINITIONS create-if-missing (hand edits
  survive), TEXTURES regenerate every run (per-ID seeds → byte-identical
  unless the generator code changed), plus guarded targeted MIGRATIONS
  (stone_pickaxe tier 1→2, log item gains PlacedBlock, tree templates move
  from generic wood/leaves onto oak/pine variants).
- **Tier ladder** (permission = RequiredToolTier): 0 hands (dirt/sand/logs/
  leaves/planks/clay/snow) · 1 wood tools (stone/cobble/cut/brick/ice) ·
  2 stone tools (copper+tin ore) · 3 copper tools (silver ore).
- **Stations**: hand → Campfire (cooking) → Workbench (stone gear, building,
  bow) → Furnace (NEW CraftingStationType, appended; ore → bars). The
  furnace prefab is the campfire pattern (CampfireBehavior fuel/fire) + a
  Furnace CraftingStationMarker.
- **New runtime hooks** (all complete, minimal): `PlayerStats` (hunger drain,
  `RestoreHunger`, eats the equipped Consumable on the use/place button —
  `ItemDefinition.HungerRestore`); ranged weapons (`IsRangedWeapon`/
  `ProjectileSpeed`/`AmmoItem` on ItemDefinition, `Projectile` manual-flight
  raycast arrows, ammo consumed per shot); `ChestBehavior` (owns a real
  `InventorySystem` — deposit equipped stack / withdraw on empty hand; the
  container UI pass will bind the existing grid view to `Storage`);
  `DoorBehavior` (root-at-hinge swing; the leaf's door_hinge socket mates
  the Phase 1 doorway socket); `BedBehavior` (night-only skip to morning via
  TimeOfDayController.SetTimeOfDay); `BoatBehavior` (possession = disable
  PlayerLocomotion + CharacterController, parent to seat, drive via
  MoveInput; kinematic waterline hold, beach-blocking; snaps up to the water
  surface on placement since liquids have no colliders).
- Starvation consequences, boat physics, container UI and world models for
  tools are the noted follow-ups — the hooks exist, nothing is stubbed.

## Cross-links between items and blocks

- Block → item: `BlockDefinition.DropItem` + `DropCountMin/Max` (what mining yields).
- Item → block: `ItemDefinition.PlacedBlock` (what a Block/Placeable item places).

## Stats (stats/attributes phase)

- **StatDefinition** follows the standard stable-ID + ScriptableObject +
  Database pattern (`StatDatabase`, synced like every other database). Assets
  live in `Assets/_Game/Content/Stats`; the code-facing IDs are centralized in
  `StatIds` (health, stamina, hunger, thirst, warmth, mining_speed,
  carry_capacity) — never scatter stat ID strings.
- **StatKind**: `Resource` = spendable pool (modifiers move the MAXIMUM;
  current depletes/regens). `Attribute` = derived number (current always
  equals the modified value).
- **Decay is negative regen** — hunger drain and stamina recovery are the one
  `regenPerSecond` field with opposite signs. Decay ignores `regenDelaySeconds`
  (the delay only gates positive regen after a decrease).
- **Modifier math**: `(base + Σflat) * (1 + Σpercent)`, clamped to
  [minValue, maxValue]. Modifiers target `Value` or `RegenRate`; sources are
  compared by reference for `RemoveAllFromSource`.
- **One container, any entity**: `StatContainer` is generic — the player lists
  all seven stats; creatures (AI phase) declare only what they need (usually
  just health) on the same component. Combat code talks to `StatContainer` /
  `IDamageable`, never to a species-specific health field.
- **Adapted hooks** (never parallel systems): PlayerLocomotion's stamina float
  → stat-backed when a container is present (serialized regen fields are
  fallback-only); PlayerStats' hunger → stat-backed + thirst
  (`ItemDefinition.ThirstRestore`); IDamageable on the player → `PlayerHealth`;
  inventory `maxCarryWeightKg` → carry_capacity stat; mining speed multiplies
  by the mining_speed stat.
- **Equip buffs**: `ItemDefinition.EquipStatModifiers` (stat ID + target +
  type + value), applied while the item is the equipped hotbar item by
  `EquippedItemStatModifiers`.
- **Cross-stat survival rules** (well-fed regen, starving penalties,
  night/campfire warmth, freezing damage) live ONLY in `PlayerSurvival`, each
  expressed as a stat modifier toggled on state edges.

## Durability (durability phase)

- **Single home**: an item's condition lives in `InventorySlot.durability01`
  (0–1, the field reserved since the inventory phase) and in
  `WorldItem.Durability01` while dropped. There is no other durability state —
  save/load serializes exactly these two.
- **Authoring**: `ItemDefinition` — `MaxDurability` (points; 0 = never
  degrades), `DurabilityPerMiningHit` / `DurabilityPerAttackHit`,
  `BreakBehavior` (Destroy | DowngradeToBrokenVariant) + `BrokenVariant`.
  `HasDurability` requires Tool/Weapon flag AND MaxDurability > 0. Broken
  variants are ordinary weaker items — no special-case runtime code.
- **Wear points, not fractions**: systems call
  `InventorySystem.ApplyDurabilityDamage(slotIndex, points)`; conversion to
  0–1 happens once, inside. Wear fires only on COMPLETED mining bites and
  weapon uses that connect (or arrows fired) — air swings are free.
- **Break at zero is immediate** (never a lingering 0-durability item):
  Destroy clears the unit, Downgrade swaps in the Broken Variant at full
  condition. `InventorySystem.ItemBroke` event for future toasts/SFX.
- **Repair**: `ItemRepair.TryRepairSlot` — cost = the item's own crafting
  recipe ingredients × workbench `repairCostFraction` × missing durability
  (min 1 each), full restore; validate-all-before-consume like crafting.
  Deliberately NO separate repair-recipe asset type. Items without a recipe
  (broken variants) are not repairable — re-craft them.
- **UI**: slot durability strip (hidden at full condition, red warning under
  25%) is part of the InventoryUIBuilder slot template — old canvases need a
  rebuild; `InventorySlotView` null-guards the bar for pre-phase canvases.

## Creatures (creatures phase)

- **CreatureDefinition** = stable ID + SO + CreatureDatabase (auto-synced).
  Species stats are `CreatureStatEntry` lists: a SHARED StatDefinition
  reference + per-species base value (0 = definition default) — never
  per-species stat assets. Loot tables (`LootTableEntry`: item, count range,
  drop chance) are authored now, ROLLED by the combat phase's death handling.
- **A creature is just another StatContainer owner**: `Creature.Init` calls
  `StatContainer.ConfigureStats(definitions, baseOverrides)` — which is also
  the full reset used on pooled reuse. Damage arrives via the existing
  IDamageable seam and lands on the health stat; `Creature.OnDeath` fires on
  the container's OnStatDepleted edge (combat phase subscribes for loot).
- **Pathing is voxel-sampled steering, NOT NavMesh**: `VoxelNavigation`
  (column sampling for ground/step/water) + `CreatureMover` (probe-and-slide
  headings, 1-block steps, gravity). Rationale documented on VoxelNavigation:
  chunk collision meshes (NavMesh's only source) exist only in the pooled
  render ring, mining edits land ~every second with no incremental NavMesh
  update, and voxel data needs zero rebake. Upgrade path if ever needed:
  A* over the same voxel data feeding the same mover.
- **AI**: CreatureAI states Idle/Wander/Alert/Flee/Chase; aggression table on
  the definition (Passive flees, Neutral reacts only to damage, Hostile
  chases). Detection = distance + optional LOS ray on a staggered 0.2 s tick.
  The combat phase adds attacking FROM Chase's approach-distance hold.
- **Spawning**: CreatureSpawner (definition, radius, max population, spawn
  interval) pools instances — bounded GameObject count; dormant beyond
  activation distance; despawns beyond despawn distance (keep that inside the
  voxel data ring). Structures/world-gen phases call SetDefinition + the same
  spawner.
- **Player stat set is explicit** (StatsSystemBuilder.PlayerStatIds) — the
  StatDatabase also holds creature stats (move_speed, attack_damage,
  detection_radius) that must not land on the player.
- Creature prefabs/animation: primitive-built generic rigs (bones + collider-
  less visuals, root capsule collider + kinematic rigidbody), editor-generated
  Idle/Walk clips (transform curves) in a Speed01 Simple1D blend.

## Creature combat (combat phase)

- **Symmetric damage**: creature attacks hit the player through the SAME
  IDamageable seam player weapons use (`PlayerHealth` on the player root);
  amount = the creature's `attack_damage` stat, type = the definition's
  `attackDamageType`. Attack timing is data (`attackWindupSeconds` = the hit
  window inside the animation, `attackCooldownSeconds` = full cycle); a
  player who backs out of range during the windup is missed.
- **Attack loop**: Chase → Attack at ApproachDistance → windup/hit/recover →
  sidestep reposition → Chase. The Attack animator state is triggered by the
  "Attack" trigger (AnyState → Attack → default on exit time); the content
  creator retrofits it onto pre-combat controllers idempotently.
- **Aggro**: an attacked Neutral counts as Hostile (`EffectiveAggression`)
  for `aggroDurationSeconds`, refreshed per hit, cleared only by successful
  escape (chase give-up) or death. Pack alerts: damage broadcasts to
  same-species creatures within `packAlertRadius` via the static CreatureAI
  registry (one broadcast, never chained) — hostiles/neutrals join, passives
  scatter.
- **Death**: Creature.OnDeath → loot table rolls into `WorldItem.Spawn`
  (the mining drop mechanism — never a second one), animator disables, the
  body tips over (scripted pose, primitive rigs have no ragdoll), despawns to
  the pool after `deathDespawnSeconds`.
- **Day/night**: CreatureSpawner subscribes OnNightStart/OnDayStart (events,
  not polling; debug SetTimeOfDay jumps fire no events by design). Night =
  `nightPopulationBonus` extra cap + `nightSpawnIntervalMultiplier` faster
  spawns + `nightDetectionRadiusBonus` as a stat MODIFIER sourced to the
  spawner (dawn strips via RemoveAllFromSource). `spawnOnlyAtNight` spawners
  sleep by day and despawn their brood at first light.
- Weapon durability wears ONLY on confirmed hits (melee connect / arrow
  fired) — verified as already implemented by the durability phase.
- Species roster: deer/goat (Passive, food loot), wolf (Neutral pack), boar
  (Hostile, night-boosted), stalker (Hostile, night-only, crafting-material
  loot).

## Structures (structures phase)

- **StructureTemplate** = stable ID + SO + StructureTemplateDatabase (synced).
  Layouts are lists of EXISTING BuildingPieceDefinitions with local pos/yaw +
  `omitChance` — the whole "ruined" effect is seeded omission of an intact
  layout (deliberately no damage-simulation or ruined-material system).
  Chest entries carry loot in the SHARED LootTableEntry format (creature
  drops = chest loot = one format). Optional spawner entries configure
  ordinary CreatureSpawners ("guarded ruins").
- **Placement** (`StructurePlacementSystem`, on the VoxelWorld object): the
  tree-scattering pattern lifted to scene objects — deterministic anchor
  cells (cellSize doubles as the spacing floor), all rolls through the
  generator's seeded hash, validated against `IslandWorldGenerator`'s
  read-only height API (SampleHeight/SeaLevelY/BeachBandSize — the noise and
  island shaping are untouched). Inland = flat grass (variance-bounded,
  origin on the highest footprint sample); Coast = shore column + open water
  at `coastSeawardExtent`, yaw chosen to POINT seaward. Cells process once
  per session as the player approaches (placeRadius stays inside the data
  ring); structures then persist as scene objects.
- **Pieces are REAL placed pieces**: instantiated under PlacedPieceRegistry
  and run through BuildingPiece.Initialize — deconstruction, weapon damage,
  durability and (Phase 6) saving treat ruins exactly like player builds.
- **Save/load note (Phase 6)**: persist placed pieces + chest inventories +
  the processed-cell set, so looted ruins don't respawn on revisit.
- Example templates: ruined_tower (guarded by nesting stalkers), ruined_dock
  (Coast), abandoned_camp (functional campfire/workbench). Menu: Island
  Game/Data/Create Example Structures + World/Add Structure System; both are
  BuildEverything steps.
