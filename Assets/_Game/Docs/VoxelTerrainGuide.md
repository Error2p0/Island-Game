# Voxel Terrain — Phase 6 (Chunks, Meshing, Mine/Place)

## Setup (one step)

1. Open the gameplay scene, run **Island Game → World → Create Voxel World**.
2. Move the player above the island surface — default ground height is 24, so
   e.g. position (0, 27, 0) — and disable any old ground plane (it would z-fight
   the terrain at its own height). Save the scene.
3. Requires the Phase 1 databases + example content (stone / wood_plank blocks with
   Read/Write-enabled textures).

Controls: **hold LMB** mine, **RMB** place the equipped Block-category item
(1–9/scroll to equip). Optional input actions "Mine"/"Place" in the Player map take
over from the mouse fallbacks, same pattern as all Phase 4/5 bindings.

## Architecture & conventions

| Piece | Role |
|---|---|
| `BlockPalette` | numeric ushort ID ↔ BlockDefinition; ID 0 = air; solidity/transparency pre-baked to flat arrays |
| `Chunk` | pure data: 16×64×16 voxels in a flattened `ushort[]` |
| `ChunkMesher` | render mesh (2 submeshes: opaque + cutout) + separate solid-only collision mesh |
| `ChunkView` | pooled GameObject: MeshFilter/Renderer/MeshCollider, owns its meshes for life |
| `VoxelWorld` | palette/atlas/materials init, streaming, `GetBlock`/`SetBlock` API |
| `DebugFlatIslandGenerator` | flat stone disc + chunk-corner marker posts (Phase 7 replaces via `IChunkGenerator`) |
| `PlayerBlockInteraction` | camera-raycast mining/placing, tier rules, drops → inventory |

- **Chunk size 16×64×16 (columns)**: 16×16 keeps per-edit remeshes cheap and makes
  coordinate math bit-ops (`>> 4`, `& 15`, negatives handled by arithmetic shift);
  height 64 covers an ocean/island game (sea level ≈ 24) in ONE vertical chunk, so
  streaming is a 2D grid and seams exist on 4 sides only.
- **Flattened 1D array**: one contiguous 32 KB allocation per chunk, cache-friendly
  y-major sweep, no jagged-array pointer chasing, blittable for the future save phase.
- **Numeric palette IDs are session-scoped**: assigned from BlockDatabase sorted by
  string ID. The save phase must write the numeric→string table with the world and
  remap on load. Never persist raw numeric IDs without the table.
- **Per-face culling, not greedy meshing**: greedy quads span blocks, needing UVs that
  tile *within* an atlas sub-rect — impossible with standard samplers; it would force a
  custom fragment shader and break Phase 1's atlas convention (plain UV-rect lookup on
  the stock Lit material). At our chunk sizes culled meshes are a few thousand tris,
  rebuilds are faster than greedy (matters: edits remesh synchronously), and the extra
  vertices are trivial. Face vertex/UV basis matches the Block Editor preview cube, so
  textures orient identically in-editor and in-world.
- **Collision** is a second, simpler mesh from the solid field only (no UVs/normals/
  transparent blocks), re-cooked on rebuild — physics never pays render costs.
- **Streaming**: data ring = renderDistance+1, mesh ring = renderDistance, and a chunk
  only meshes when all 4 neighbors have data — border culling always sees real blocks,
  so no gaps or phantom walls at seams. Both stages budgeted per frame (defaults: 6
  data / 2 mesh). Far chunks release pooled views; chunk *data* stays resident so
  edits persist for the session.
- **Edits**: `SetBlock` remeshes the edited chunk synchronously, plus the touching
  neighbor when the block is on a chunk border. Never a full-world rebuild.

## Targeting & break feedback (BlockTargetIndicator)

- A thin wireframe box outlines the block under the crosshair whenever one is in
  reach — **white** = the equipped tier can mine it, **red** = tier-blocked or
  unbreakable (the visible form of the blocked-outright policy below).
- While mining, a crack overlay on the block steps through cumulative stages with
  `MiningProgress01`. Both visuals are generated at runtime (no assets to author)
  and configurable on the player's `BlockTargetIndicator` (colors, line width,
  stage count). Added automatically by Island Game → World → Create Voxel World.

## Mining rules (documented policy)

- Permission: `RequiredToolTier > equipped tier` (bare hands / non-tools = tier 0) →
  mining is **blocked outright**, not slowed — clearest feedback without UI, matches
  the survival references. `Unbreakable` behavior flag always blocks.
- Speed: `Hardness` seconds baseline; equipped tool with the block in its Efficient
  Blocks list divides by `MiningSpeedMultiplier`.
- Drops: `DropItem` × random(DropCountMin..Max) → `AddItem`; overflow spawns as a
  `WorldItem` at the mined cell.
- `PlayerBlockInteraction.MiningProgress01` is the hook for a crack overlay/HUD later.

## Performance characteristics

Rebuild cost is one 16 384-voxel sweep + face emission (~1–3 ms worst case on the main
thread); an edit re-cooks 1–2 chunk colliders. Default render distance 6 ≈ 169 meshed
chunks; empty ocean chunks (NonAirCount 0) skip the sweep entirely. If profiling ever
shows meshing spikes, the budgets are the first knob and the mesher is single-instance/
allocation-free, ready to move off-thread in a later phase.

## Test checklist

1. **Streaming**: enter play above the island — nearby chunks appear first, horizon
   fills in. Walk toward the island edge and back: far views release, returning
   re-meshes instantly (Hierarchy: pooled "Chunk" objects toggle active).
2. **Seams**: walk the whole island — no gaps, no double faces at chunk borders
   (corner marker posts show exactly where borders are).
3. **Mine**: bare-handed, hold LMB on stone (tier 1) — nothing happens (blocked).
   Give yourself a tier ≥1 pickaxe via the creative menu (F1) with stone in its
   Efficient Blocks, equip it — stone breaks in Hardness/multiplier seconds and Stone
   items enter the inventory (or fall as world items when full). Wood plank markers
   (tier 0) mine bare-handed.
4. **Place**: equip Stone (Block category), RMB on terrain — block appears on the
   aimed face, stack count drops by one. Try placing into your own feet — refused.
5. **Cross-chunk edits**: mine/place along a marker post's chunk border — both sides
   update the same frame, no holes, no leftover faces (this is the SetBlock
   neighbor-remesh path).
6. **Edit persistence (session)**: dig a pit, sprint away until the chunk unloads,
   return — the pit is still there (data retained; disk saves are a later phase).
