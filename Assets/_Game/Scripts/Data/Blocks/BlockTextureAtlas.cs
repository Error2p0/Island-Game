using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace IslandGame.Data.Blocks
{
    /// <summary>
    /// Runtime-built texture atlas for block rendering.
    ///
    /// THE ATLAS CONVENTION (Phase 2's block editor and Phase 6's terrain
    /// mesher both depend on this — do not deviate):
    ///   1. BlockDefinitions reference plain Texture2D assets per face. Nothing
    ///      about atlas layout is ever authored or serialized; designers just
    ///      assign textures and the editor tools preview those same textures.
    ///   2. At world load, the terrain system calls Build() once with
    ///      BlockDatabase.Instance.All. Packing happens then; the terrain
    ///      material samples the resulting Texture, and the mesher fetches each
    ///      face's UV rect with GetUVRect(block, face) while building chunks.
    ///   3. UV rects live only in memory. Terrain saves store block IDs, so
    ///      adding/removing/reordering block textures between sessions can
    ///      never corrupt a saved world — the next Build() simply lays the
    ///      atlas out differently and the mesher picks up the new rects.
    ///   4. Source texture requirements: Read/Write enabled in import settings
    ///      (packing reads pixels), square, ideally all the same size (16/32/64)
    ///      — mixed sizes pack fine but waste space. Point filtering is applied
    ///      to the atlas for the crisp voxel look.
    ///   5. A face with no texture, or with a non-readable texture, resolves to
    ///      a built-in magenta/black error tile so mistakes are loudly visible
    ///      in-world instead of silently invisible.
    /// </summary>
    public sealed class BlockTextureAtlas
    {
        private readonly Dictionary<Texture2D, Rect> rectByTexture = new Dictionary<Texture2D, Rect>();
        private Rect errorRect;

        /// <summary>The packed atlas. Assign to the terrain material's main texture.</summary>
        public Texture2D Texture { get; private set; }

        /// <summary>UV rect of the magenta/black error tile (also returned for unresolvable faces).</summary>
        public Rect ErrorRect => errorRect;

        /// <summary>Number of distinct source textures packed (excluding the error tile).</summary>
        public int PackedTextureCount => rectByTexture.Count;

        private BlockTextureAtlas()
        {
        }

        /// <summary>
        /// Packs every texture referenced by the given blocks into one atlas.
        /// Call once per world load with BlockDatabase.Instance.All. Problems
        /// (non-readable textures, blocks with no textures, packing overflow)
        /// are logged with asset names and mapped to the error tile — Build
        /// always returns a usable atlas.
        /// </summary>
        public static BlockTextureAtlas Build(IReadOnlyList<BlockDefinition> blocks, int padding = 2, int maximumAtlasSize = 4096)
        {
            var atlas = new BlockTextureAtlas();

            var uniqueTextures = new List<Texture2D>();
            var seen = new HashSet<Texture2D>();
            var unreadable = new List<string>();
            var texturelessBlocks = new List<string>();

            if (blocks != null)
            {
                foreach (BlockDefinition block in blocks)
                {
                    if (block == null)
                        continue;

                    bool anyTexture = false;
                    foreach (BlockFace face in BlockFaces.All)
                    {
                        Texture2D texture = block.GetFaceTexture(face);
                        if (texture == null)
                            continue;

                        anyTexture = true;
                        if (!seen.Add(texture))
                            continue;

                        if (!texture.isReadable)
                            unreadable.Add(texture.name);
                        else
                            uniqueTextures.Add(texture);
                    }

                    if (!anyTexture)
                        texturelessBlocks.Add($"{block.name} ({block.Id})");
                }
            }

            if (unreadable.Count > 0)
                Debug.LogError(
                    "BlockTextureAtlas: these textures are not Read/Write enabled and will show the error tile — " +
                    "fix their import settings: " + string.Join(", ", unreadable));

            if (texturelessBlocks.Count > 0)
                Debug.LogWarning(
                    "BlockTextureAtlas: these blocks have no textures assigned and will render the error tile: " +
                    string.Join(", ", texturelessBlocks));

            // The error tile is packed like any other texture; its rect answers
            // every lookup that can't resolve to a real one.
            Texture2D errorTile = CreateErrorTile();
            var sources = new Texture2D[uniqueTextures.Count + 1];
            sources[0] = errorTile;
            for (int i = 0; i < uniqueTextures.Count; i++)
                sources[i + 1] = uniqueTextures[i];

            var packed = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = "BlockAtlas",
            };

            Rect[] uvRects = packed.PackTextures(sources, padding, maximumAtlasSize, makeNoLongerReadable: false);
            DestroyCompat(errorTile);

            if (uvRects == null || uvRects.Length != sources.Length)
            {
                Debug.LogError(
                    $"BlockTextureAtlas: packing {sources.Length} textures failed (over {maximumAtlasSize}px? unsupported formats?). " +
                    "All faces will render the error tile.");
                DestroyCompat(packed);
                atlas.Texture = CreateErrorTile();
                atlas.Texture.name = "BlockAtlas (packing failed)";
                atlas.errorRect = new Rect(0f, 0f, 1f, 1f);
                return atlas;
            }

            packed.filterMode = FilterMode.Point;
            packed.wrapMode = TextureWrapMode.Clamp;

            atlas.Texture = packed;
            atlas.errorRect = uvRects[0];
            for (int i = 0; i < uniqueTextures.Count; i++)
                atlas.rectByTexture.Add(uniqueTextures[i], uvRects[i + 1]);

            Debug.Log($"BlockTextureAtlas: packed {uniqueTextures.Count} texture(s) into {packed.width}x{packed.height}.");
            return atlas;
        }

        /// <summary>
        /// UV rect for one face of one block, for the terrain mesher's vertex
        /// UVs. Unresolvable faces (no texture / unpacked texture / null block)
        /// return the error tile — this never throws mid-meshing.
        /// </summary>
        public Rect GetUVRect(BlockDefinition block, BlockFace face)
        {
            Texture2D texture = block != null ? block.GetFaceTexture(face) : null;

            if (texture != null && rectByTexture.TryGetValue(texture, out Rect rect))
                return rect;

            return errorRect;
        }

        /// <summary>Frees the atlas texture. Call when tearing down the world (the source textures are untouched assets).</summary>
        public void Release()
        {
            DestroyCompat(Texture);
            Texture = null;
            rectByTexture.Clear();
        }

        /// <summary>Multi-line layout report for debugging and the editor preview tool.</summary>
        public string DescribeLayout(IReadOnlyList<BlockDefinition> blocks)
        {
            var builder = new StringBuilder();
            builder.AppendLine(Texture != null
                ? $"BlockTextureAtlas {Texture.width}x{Texture.height}, {PackedTextureCount} texture(s), error tile at {errorRect}."
                : "BlockTextureAtlas (released).");

            if (blocks == null)
                return builder.ToString();

            foreach (BlockDefinition block in blocks)
            {
                if (block == null)
                    continue;

                builder.AppendLine($"  {block.name} ({block.Id}):");
                foreach (BlockFace face in BlockFaces.All)
                {
                    Rect rect = GetUVRect(block, face);
                    string suffix = rect == errorRect ? " [ERROR TILE]" : string.Empty;
                    builder.AppendLine($"    {face,-6} -> {rect}{suffix}");
                }
            }

            return builder.ToString();
        }

        private static Texture2D CreateErrorTile()
        {
            const int size = 16;
            const int checker = 8;
            var magenta = new Color32(255, 0, 255, 255);
            var black = new Color32(0, 0, 0, 255);

            var tile = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "BlockAtlasErrorTile",
                filterMode = FilterMode.Point,
            };

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = (x / checker + y / checker) % 2 == 0 ? magenta : black;
            }

            tile.SetPixels32(pixels);
            tile.Apply(false, false);
            return tile;
        }

        private static void DestroyCompat(Object target)
        {
            if (target == null)
                return;

            // The atlas is built both in play mode and from editor tools
            // (Phase 2 previews); Destroy is illegal in edit mode.
            if (Application.isPlaying)
                Object.Destroy(target);
            else
                Object.DestroyImmediate(target);
        }
    }
}
