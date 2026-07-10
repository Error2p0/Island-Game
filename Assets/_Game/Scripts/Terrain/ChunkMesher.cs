using System.Collections.Generic;
using IslandGame.Data.Blocks;
using UnityEngine;
using UnityEngine.Rendering;

namespace IslandGame.Terrain
{
    /// <summary>
    /// Builds a chunk's render and collision meshes with per-face culling: a
    /// face is emitted only where a block meets air or a transparent block of
    /// a different type, so interior faces never exist.
    ///
    /// WHY CULLED FACES AND NOT GREEDY MESHING: greedy-merged quads span
    /// multiple blocks, which requires UVs that TILE within an atlas sub-rect —
    /// impossible with a packed atlas and standard samplers; it needs a custom
    /// fragment shader doing frac() lookups, breaking Phase 1's atlas
    /// convention (plain UV-rect lookups on a standard Lit material). At this
    /// game's chunk size (16×64×16, surface-dominated islands) culled meshes
    /// are a few thousand triangles per chunk, rebuilds are FASTER than greedy
    /// (no merge pass) which matters because block edits remesh synchronously,
    /// and the vertex overhead is trivial for modern GPUs.
    ///
    /// Render mesh: submesh 0 = opaque blocks, submesh 1 = transparent blocks
    /// (alpha-clip cutout material — leaves/glass style). UVs come straight
    /// from BlockTextureAtlas.GetUVRect per face; the face vertex/UV basis is
    /// identical to the Block Editor preview cube, so textures orient the same
    /// in-editor and in-world.
    ///
    /// Collision mesh (requirement 2a): built in the same voxel sweep but from
    /// the SOLID field only — no UVs, no normals, no materials, no transparent
    /// blocks — so physics never pays render-mesh costs.
    ///
    /// SUB-VOXEL PASS (small-voxel phase): promoted cells skip base-face
    /// emission entirely and instead emit their filled sub-cells with the same
    /// per-face culling at 1/res scale, into the SAME buffers/submeshes — one
    /// extra pass, one mesh, one collider. Seam rules:
    ///   • sub-face against a sub-cell (same grid, or the touching sub-cell of
    ///     an adjacent promoted cell — also across chunk borders): culled when
    ///     that sub-cell is filled;
    ///   • sub-face against a normal block: the base visibility rules apply
    ///     (occluded by solid opaque, drawn against air/transparent);
    ///   • a NORMAL block's face against a promoted neighbor: culled only when
    ///     the neighbor grid's touching boundary layer is completely full,
    ///     otherwise the full face is drawn behind the detail — slight hidden
    ///     overdraw, but carving can never open a see-through gap.
    /// Sub-cell collision quads join the chunk's existing mesh collider: the
    /// alternatives (per-sub-cell box colliders = hundreds of components per
    /// damaged block; a single box = can't walk into carved holes) lose to
    /// re-cooking the collider we already re-cook on every edit, since damage
    /// is localized. The pristine path costs nothing: one HasPromotionsNear
    /// check per rebuild gates every per-cell lookup.
    ///
    /// UV SLICING (texture continuity): a promoted cell's face rect from the
    /// atlas is divided into res×res sub-rects; each sub-face samples the
    /// sub-rect at its (u,v) index — the projection of the sub-cell's integer
    /// coords onto that face's existing texture axes (FaceU/FaceV below), so
    /// a half-carved stone block still reads as THAT stone texture with a
    /// bite taken out. See SliceFaceUV for the per-face index math.
    ///
    /// Single instance, reused per rebuild: all buffers are allocated once.
    /// </summary>
    public sealed class ChunkMesher
    {
        /// <summary>Neighbor sample meaning "below the world floor" — never render or collide that face.</summary>
        private const ushort Occluded = ushort.MaxValue;

        // Face tables in BlockFace order (Top,Bottom,North,South,East,West).
        // v = texture-up, u = Cross(n, v) = texture-right viewed from outside;
        // same math as BlockCubePreview so authored textures match in-world.
        private static readonly Vector3Int[] FaceNormals =
        {
            new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0),
            new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1),
            new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0),
        };

        private static readonly Vector3[] FaceN =
        {
            Vector3.up, Vector3.down, Vector3.forward, Vector3.back, Vector3.right, Vector3.left,
        };

        private static readonly Vector3[] FaceU =
        {
            new Vector3(1, 0, 0), new Vector3(-1, 0, 0),
            new Vector3(-1, 0, 0), new Vector3(1, 0, 0),
            new Vector3(0, 0, 1), new Vector3(0, 0, -1),
        };

        private static readonly Vector3[] FaceV =
        {
            new Vector3(0, 0, 1), new Vector3(0, 0, 1),
            new Vector3(0, 1, 0), new Vector3(0, 1, 0),
            new Vector3(0, 1, 0), new Vector3(0, 1, 0),
        };

        private readonly List<Vector3> vertices = new List<Vector3>(4096);
        private readonly List<Vector3> normals = new List<Vector3>(4096);
        private readonly List<Vector2> uvs = new List<Vector2>(4096);
        private readonly List<int> opaqueTriangles = new List<int>(6144);
        private readonly List<int> cutoutTriangles = new List<int>(512);
        private readonly List<Vector3> collisionVertices = new List<Vector3>(4096);
        private readonly List<int> collisionTriangles = new List<int>(6144);

        private readonly Rect[] cellFaceRects = new Rect[6]; // per-promoted-cell scratch
        private bool promotionsNearby;

        /// <summary>True when the last Build produced any collision geometry (empty meshes must not reach MeshCollider).</summary>
        public bool HasCollision => collisionTriangles.Count > 0;

        /// <summary>Rebuilds both meshes in place from the chunk's current data.</summary>
        public void Build(
            Chunk chunk, VoxelWorld world, BlockPalette palette, BlockTextureAtlas atlas,
            Mesh renderMesh, Mesh collisionMesh)
        {
            vertices.Clear();
            normals.Clear();
            uvs.Clear();
            opaqueTriangles.Clear();
            cutoutTriangles.Clear();
            collisionVertices.Clear();
            collisionTriangles.Clear();

            // One check per rebuild keeps the pristine hot path lookup-free.
            promotionsNearby = world.HasPromotionsNear(chunk.Coord);

            if (chunk.NonAirCount > 0)
                SweepVoxels(chunk, world, palette, atlas);

            renderMesh.Clear();
            renderMesh.indexFormat = IndexFormat.UInt32;
            renderMesh.SetVertices(vertices);
            renderMesh.SetNormals(normals);
            renderMesh.SetUVs(0, uvs);
            renderMesh.subMeshCount = 2;
            renderMesh.SetTriangles(opaqueTriangles, 0);
            renderMesh.SetTriangles(cutoutTriangles, 1);

            collisionMesh.Clear();
            collisionMesh.indexFormat = IndexFormat.UInt32;
            collisionMesh.SetVertices(collisionVertices);
            collisionMesh.SetTriangles(collisionTriangles, 0);
        }

        private void SweepVoxels(Chunk chunk, VoxelWorld world, BlockPalette palette, BlockTextureAtlas atlas)
        {
            int originX = chunk.Coord.x * Chunk.SizeX;
            int originZ = chunk.Coord.y * Chunk.SizeZ;

            for (int y = 0; y < Chunk.SizeY; y++)
            {
                for (int z = 0; z < Chunk.SizeZ; z++)
                {
                    for (int x = 0; x < Chunk.SizeX; x++)
                    {
                        ushort id = chunk.Get(x, y, z);
                        if (id == BlockPalette.AirId)
                            continue;

                        BlockDefinition definition = palette.GetDefinition(id);
                        List<int> renderTriangles = palette.IsTransparent(id) ? cutoutTriangles : opaqueTriangles;
                        bool solid = palette.IsSolid(id);

                        // Promoted cell: the detail pass replaces the base
                        // faces entirely (a big flat face would z-fight the
                        // sub-voxel geometry underneath).
                        if (promotionsNearby && chunk.TryGetSubVoxels(x, y, z, out SubVoxelGrid grid))
                        {
                            EmitSubVoxelCell(
                                chunk, world, palette, atlas, originX, originZ,
                                x, y, z, id, definition, grid, renderTriangles, solid);
                            continue;
                        }

                        for (int face = 0; face < BlockFaces.Count; face++)
                        {
                            Vector3Int n = FaceNormals[face];
                            ushort neighbor = Sample(chunk, world, originX, originZ, x + n.x, y + n.y, z + n.z);

                            // A promoted neighbor with a fully-filled boundary
                            // layer occludes exactly like an intact block; any
                            // hole means our full face must render behind it.
                            SubVoxelGrid neighborGrid = promotionsNearby
                                ? GridAt(chunk, world, originX, originZ, x + n.x, y + n.y, z + n.z)
                                : null;
                            bool neighborHasHoles = neighborGrid != null && !neighborGrid.IsFaceLayerFull(face ^ 1);

                            if (RenderFaceVisible(id, neighbor, palette) || neighborHasHoles)
                            {
                                Rect uvRect = atlas.GetUVRect(definition, BlockFaces.All[face]);
                                EmitRenderFace(x, y, z, face, uvRect, renderTriangles);
                            }

                            if (solid && (CollisionFaceVisible(neighbor, palette) || neighborHasHoles))
                                EmitCollisionFace(x, y, z, face);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Neighbor lookup: inside the chunk reads local data; below the world
        /// returns Occluded (no face); above returns air; across a chunk border
        /// asks the world (unloaded neighbor = air — VoxelWorld only meshes
        /// chunks whose four neighbors have data, so borders are always true).
        /// </summary>
        private static ushort Sample(Chunk chunk, VoxelWorld world, int originX, int originZ, int x, int y, int z)
        {
            if (y < 0)
                return Occluded;
            if (y >= Chunk.SizeY)
                return BlockPalette.AirId;
            if (x >= 0 && x < Chunk.SizeX && z >= 0 && z < Chunk.SizeZ)
                return chunk.Get(x, y, z);

            return world.GetBlockId(originX + x, y, originZ + z);
        }

        private static bool RenderFaceVisible(ushort self, ushort neighbor, BlockPalette palette)
        {
            if (neighbor == Occluded)
                return false;
            if (neighbor == BlockPalette.AirId)
                return true;
            if (!palette.IsTransparent(neighbor))
                return false;

            // Transparent neighbor: draw unless it's the same block type
            // (glass panes / leaf clusters don't render their internal walls).
            return neighbor != self;
        }

        private static bool CollisionFaceVisible(ushort neighbor, BlockPalette palette)
        {
            if (neighbor == Occluded)
                return false;

            return neighbor == BlockPalette.AirId || !palette.IsSolid(neighbor);
        }

        // ------------------------------------------------------------------
        // Sub-voxel pass (small-voxel phase)
        // ------------------------------------------------------------------

        /// <summary>
        /// Emits every filled sub-cell of one promoted cell with per-face
        /// culling at 1/res scale — the exact base algorithm, one octave down.
        /// Boundary sub-faces sample the neighbor BLOCK: another promoted
        /// grid is sampled per sub-cell (seam stitching, also across chunk
        /// borders), a normal block uses the base visibility rules.
        /// </summary>
        private void EmitSubVoxelCell(
            Chunk chunk, VoxelWorld world, BlockPalette palette, BlockTextureAtlas atlas,
            int originX, int originZ, int x, int y, int z,
            ushort id, BlockDefinition definition, SubVoxelGrid grid, List<int> renderTriangles, bool solid)
        {
            int res = grid.Resolution;
            float subSize = 1f / res;
            float halfSub = subSize * 0.5f;

            // The six base rects once per cell — every sub-face slices these.
            for (int face = 0; face < BlockFaces.Count; face++)
                cellFaceRects[face] = atlas.GetUVRect(definition, BlockFaces.All[face]);

            for (int sy = 0; sy < res; sy++)
            {
                for (int sz = 0; sz < res; sz++)
                {
                    for (int sx = 0; sx < res; sx++)
                    {
                        if (!grid.Get(sx, sy, sz))
                            continue;

                        var center = new Vector3(
                            x + (sx + 0.5f) * subSize,
                            y + (sy + 0.5f) * subSize,
                            z + (sz + 0.5f) * subSize);

                        for (int face = 0; face < BlockFaces.Count; face++)
                        {
                            Vector3Int n = FaceNormals[face];
                            int nsx = sx + n.x;
                            int nsy = sy + n.y;
                            int nsz = sz + n.z;

                            bool renderVisible;
                            bool collideVisible;

                            if (nsx >= 0 && nsx < res && nsy >= 0 && nsy < res && nsz >= 0 && nsz < res)
                            {
                                // Interior neighbor: same grid.
                                bool filled = grid.Get(nsx, nsy, nsz);
                                renderVisible = !filled;
                                collideVisible = !filled;
                            }
                            else
                            {
                                // Crossing into the neighbor block cell.
                                int bx = x + n.x;
                                int by = y + n.y;
                                int bz = z + n.z;
                                ushort neighborId = Sample(chunk, world, originX, originZ, bx, by, bz);

                                if (neighborId == Occluded)
                                {
                                    renderVisible = false;
                                    collideVisible = false;
                                }
                                else
                                {
                                    SubVoxelGrid neighborGrid = promotionsNearby
                                        ? GridAt(chunk, world, originX, originZ, bx, by, bz)
                                        : null;

                                    if (neighborGrid != null)
                                    {
                                        // Adjacent promoted cell: sample the
                                        // touching sub-cell (proportional index
                                        // mapping tolerates mixed resolutions).
                                        int nres = neighborGrid.Resolution;
                                        bool filled = neighborGrid.Get(
                                            WrapSubCoord(nsx, res, nres),
                                            WrapSubCoord(nsy, res, nres),
                                            WrapSubCoord(nsz, res, nres));
                                        renderVisible = !filled;
                                        collideVisible = !filled;
                                    }
                                    else
                                    {
                                        renderVisible = RenderFaceVisible(id, neighborId, palette);
                                        collideVisible = CollisionFaceVisible(neighborId, palette);
                                    }
                                }
                            }

                            if (renderVisible)
                            {
                                Rect subRect = SliceFaceUV(cellFaceRects[face], face, sx, sy, sz, res);
                                EmitScaledRenderFace(center, halfSub, face, subRect, renderTriangles);
                            }

                            if (solid && collideVisible)
                                EmitScaledCollisionFace(center, halfSub, face);
                        }
                    }
                }
            }
        }

        /// <summary>Promoted grid at a chunk-local position (may cross into a neighbor chunk via the world). Null when not promoted.</summary>
        private static SubVoxelGrid GridAt(Chunk chunk, VoxelWorld world, int originX, int originZ, int x, int y, int z)
        {
            if (y < 0 || y >= Chunk.SizeY)
                return null;

            if (x >= 0 && x < Chunk.SizeX && z >= 0 && z < Chunk.SizeZ)
                return chunk.TryGetSubVoxels(x, y, z, out SubVoxelGrid grid) ? grid : null;

            return world.GetSubVoxelGrid(originX + x, y, originZ + z);
        }

        /// <summary>
        /// Maps a sub-coordinate that stepped over a grid edge into the
        /// adjacent grid: one past the end wraps to that grid's first layer,
        /// -1 wraps to its last; in-range axes map proportionally so grids of
        /// different resolutions (setting changed mid-session) still stitch.
        /// </summary>
        private static int WrapSubCoord(int coord, int res, int neighborRes)
        {
            if (coord < 0)
                return neighborRes - 1;
            if (coord >= res)
                return 0;
            return coord * neighborRes / res;
        }

        /// <summary>
        /// TEXTURE CONTINUITY: the sub-rect of the block face's atlas rect
        /// that one sub-face must sample. The face's texture axes are fixed by
        /// FaceU/FaceV (u = texture-right seen from outside, v = texture-up);
        /// projecting the sub-cell's integer coords onto those axes gives its
        /// (uIndex, vIndex) tile in the res×res slice grid — axes that run
        /// NEGATIVE along a world axis flip their index (res-1-c). Depth along
        /// the face normal doesn't appear in the projection, which is exactly
        /// why interior sub-faces exposed by carving show the same texels the
        /// surface showed above them: "stone with a chunk missing".
        /// </summary>
        private static Rect SliceFaceUV(Rect baseRect, int face, int sx, int sy, int sz, int res)
        {
            int uIndex;
            int vIndex;
            switch (face)
            {
                case 0: uIndex = sx; vIndex = sz; break;                // Top:    u=+X, v=+Z
                case 1: uIndex = res - 1 - sx; vIndex = sz; break;      // Bottom: u=−X, v=+Z
                case 2: uIndex = res - 1 - sx; vIndex = sy; break;      // North:  u=−X, v=+Y
                case 3: uIndex = sx; vIndex = sy; break;                // South:  u=+X, v=+Y
                case 4: uIndex = sz; vIndex = sy; break;                // East:   u=+Z, v=+Y
                default: uIndex = res - 1 - sz; vIndex = sy; break;     // West:   u=−Z, v=+Y
            }

            float du = baseRect.width / res;
            float dv = baseRect.height / res;
            return new Rect(baseRect.xMin + uIndex * du, baseRect.yMin + vIndex * dv, du, dv);
        }

        private void EmitScaledRenderFace(Vector3 center, float halfSize, int face, Rect uvRect, List<int> triangles)
        {
            int baseIndex = vertices.Count;
            AddFaceVertices(vertices, center, halfSize, face);

            Vector3 normal = FaceN[face];
            for (int i = 0; i < 4; i++)
                normals.Add(normal);

            uvs.Add(new Vector2(uvRect.xMin, uvRect.yMin));
            uvs.Add(new Vector2(uvRect.xMin, uvRect.yMax));
            uvs.Add(new Vector2(uvRect.xMax, uvRect.yMax));
            uvs.Add(new Vector2(uvRect.xMax, uvRect.yMin));

            AddQuadIndices(triangles, baseIndex);
        }

        private void EmitScaledCollisionFace(Vector3 center, float halfSize, int face)
        {
            int baseIndex = collisionVertices.Count;
            AddFaceVertices(collisionVertices, center, halfSize, face);
            AddQuadIndices(collisionTriangles, baseIndex);
        }

        // ------------------------------------------------------------------
        // Face emission
        // ------------------------------------------------------------------

        private void EmitRenderFace(int x, int y, int z, int face, Rect uvRect, List<int> triangles)
        {
            EmitScaledRenderFace(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f), 0.5f, face, uvRect, triangles);
        }

        private void EmitCollisionFace(int x, int y, int z, int face)
        {
            EmitScaledCollisionFace(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f), 0.5f, face);
        }

        private static void AddFaceVertices(List<Vector3> target, Vector3 center, float halfSize, int face)
        {
            Vector3 n = FaceN[face];
            Vector3 u = FaceU[face];
            Vector3 v = FaceV[face];

            // Corner order (u,v): (0,0) (0,1) (1,1) (1,0) — matches the UV writes.
            target.Add(center + (n - u - v) * halfSize);
            target.Add(center + (n - u + v) * halfSize);
            target.Add(center + (n + u + v) * halfSize);
            target.Add(center + (n + u - v) * halfSize);
        }

        private static void AddQuadIndices(List<int> triangles, int baseIndex)
        {
            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex + 3);
        }
    }
}
