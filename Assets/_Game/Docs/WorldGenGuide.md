# World Generation — Phase 7 (Islands & Ocean)

## Setup

1. **Island Game → Data → Create World Gen Content** — creates Sand, Dirt, Grass
   (per-face: green top / dirt bottom / blended sides) and Water (non-solid,
   transparent, Liquid flag) blocks, their textures, and Sand/Dirt/Grass Block items.
   Idempotent; everything is editable afterwards in the Block/Item Editors.
2. **Island Game → World → Create Voxel World** (re-run in the existing scene: it now
   also adds and wires `PlayerVoxelWaterSensor` on the player).
3. Position the player above the origin — a spawn island is guaranteed there
   (Force Island At Origin) — e.g. `(0, 45, 0)`, and save the scene.

## How the shaping works (and what to tune)

Two independent seeded noise layers on `IslandWorldGenerator` (VoxelWorld inspector):

- **Island mask** (2-octave fbm, `Island Mask Frequency` ≈ 0.005): decides *where land
  exists*. `Land Threshold` (0.62) keeps land a minority — raise it for emptier oceans,
  lower the frequency for larger, rarer islands. `Coast Falloff` is the band above the
  threshold where a smoothstepped land factor blends column height from ocean floor up
  to full island height — that's what makes real sloped coastlines instead of a
  noise-textured cliff at a cutoff.
- **Height** (4-octave fbm, `Height Frequency`, `Max Island Height`): hills *on* the
  islands. Ocean floor = `Sea Level − Ocean Depth` + gentle floor noise.
- **Surface layers**: column tops within `Beach Band` of sea level (and all underwater
  floors) are sand over stone; higher land is grass / dirt (`Dirt Depth`) / stone.
  Water fills every column from its floor to `Sea Level` (24 → surface plane Y=24.0).
- **Determinism**: `Seed` → `System.Random` → per-octave domain offsets for
  `Mathf.PerlinNoise` (chosen: zero dependencies, ample quality for octave-stacked
  heightfields; it lacks a seed input, hence domain offsetting, and it's deterministic
  per Unity version/platform). Same seed = same world. Swap to Unity.Mathematics noise
  behind `IChunkGenerator` if 3D features (caves) are ever needed.
- `Use Debug Flat Generator` on VoxelWorld flips back to the Phase 6 test island.

## Water ↔ movement system (the important connection)

Chosen approach: **voxel-data water detection**, not generated trigger volumes —
ocean bodies span hundreds of chunks and change with every mined/placed block, so
keeping collider volumes in sync would be a permanent bug source; a per-frame column
probe is always current and near-free.

- `PlayerVoxelWaterSensor` (player): if the block at the feet has the Liquid flag, it
  scans up to the first non-liquid block (= surface Y) and calls the new additive hook
  `PlayerLocomotion.SetExternalWater(inWater, surfaceY)`.
- `PlayerLocomotion` combines that with the untouched Phase 5 trigger-volume list —
  **highest surface among all sources wins** — then all existing behavior (wade
  slowdown by depth, swim enter 1.35 m / exit 1.15 m hysteresis, buoyancy float line,
  eye-underwater, carry-weight water penalties) runs unchanged. Hand-placed trigger
  pools still work in parallel.

Water is rendered on the transparent submesh, which is now true alpha **blending**
(was alpha-clip cutout) so the ocean reads translucent from above; from below the
surface is backface-culled (no underwater ceiling plane — acceptable, polish later).
No fluid simulation yet: water is static volume data; placing a block into a water
cell replaces it, mining next to water leaves a dry hole.

## Persistence behavior (requirement 6)

Generation only runs for chunks not already in the world dictionary, and chunk data
never unloads — so player edits are never regenerated over within a session.
`Chunk.IsGenerated` / `Chunk.IsModified` mark exactly which chunks the future
save/load phase must persist (generator writes don't count as modifications).

## Test checklist

1. **Islands + ocean**: enter play, open the Scene view (or swim): mostly ocean,
   scattered islands of varied size with sand coastlines, grass tops, dirt under the
   turf, stone cores. Change `Seed`, restart — different world; same seed — identical.
2. **Swimming**: walk off the spawn beach — wading slows you with depth, then the
   swim state engages; dive (Crouch), surface (Space), watch buoyancy settle at the
   float line. Climb back out on the beach slope — state exits cleanly.
3. **Mine/place still works**: dig through grass → dirt → stone (grass and dirt drop
   Dirt, sand drops Sand); place blocks into the water at the shore to build a pier —
   water cells are replaceable.
4. **Streaming**: swim toward the horizon — new islands appear seamlessly; return home
   — any holes you dug are still there.
5. **Trigger-volume compatibility**: the old Phase 5 water test volume (if still in
   the scene) still swims exactly as before, coexisting with the ocean.
