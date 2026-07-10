using System.Collections.Generic;
using IslandGame.Data.Blocks;
using UnityEngine;

namespace IslandGame.Terrain
{
    /// <summary>
    /// Maps BlockDefinitions to compact numeric IDs for voxel storage. Chunks
    /// store ushorts (2 bytes/voxel), never ScriptableObject references — the
    /// full definition is looked up here when meshing or mining needs it.
    /// ID 0 is always air. IDs are assigned from the BlockDatabase sorted by
    /// string ID, so they are deterministic for a given block set — but they
    /// are SESSION-SCOPED: the future save phase must serialize this
    /// numeric→string table with the world and remap on load, because adding
    /// or removing block assets shifts the numeric assignment.
    ///
    /// Solidity/transparency are pre-baked into flat arrays so the mesher's
    /// hot loop never touches ScriptableObjects.
    /// </summary>
    public sealed class BlockPalette
    {
        public const ushort AirId = 0;

        private readonly List<BlockDefinition> definitionsById = new List<BlockDefinition>();
        private readonly Dictionary<BlockDefinition, ushort> idByDefinition = new Dictionary<BlockDefinition, ushort>();
        private readonly Dictionary<string, ushort> idByStringId = new Dictionary<string, ushort>();
        private bool[] transparentById;
        private bool[] solidById;

        /// <summary>Number of entries including air.</summary>
        public int Count => definitionsById.Count;

        public static BlockPalette Build(IReadOnlyList<BlockDefinition> blocks)
        {
            var palette = new BlockPalette();
            palette.definitionsById.Add(null); // 0 = air

            var sorted = new List<BlockDefinition>();
            for (int i = 0; i < blocks.Count; i++)
            {
                if (blocks[i] != null)
                    sorted.Add(blocks[i]);
            }

            sorted.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

            foreach (BlockDefinition block in sorted)
            {
                if (palette.idByDefinition.ContainsKey(block))
                    continue; // defensive; database sync already reports duplicates

                var id = (ushort)palette.definitionsById.Count;
                palette.definitionsById.Add(block);
                palette.idByDefinition.Add(block, id);
                if (!string.IsNullOrEmpty(block.Id) && !palette.idByStringId.ContainsKey(block.Id))
                    palette.idByStringId.Add(block.Id, id);
            }

            palette.transparentById = new bool[palette.definitionsById.Count];
            palette.solidById = new bool[palette.definitionsById.Count];
            palette.transparentById[AirId] = true;
            for (int i = 1; i < palette.definitionsById.Count; i++)
            {
                palette.transparentById[i] = palette.definitionsById[i].IsTransparent;
                palette.solidById[i] = palette.definitionsById[i].IsSolid;
            }

            return palette;
        }

        /// <summary>Numeric ID for a definition; AirId when null or not in the palette.</summary>
        public ushort GetId(BlockDefinition block)
        {
            return block != null && idByDefinition.TryGetValue(block, out ushort id) ? id : AirId;
        }

        /// <summary>Numeric ID by string ID (generators resolve their configured blocks once via this).</summary>
        public bool TryGetIdByStringId(string stringId, out ushort id)
        {
            if (!string.IsNullOrEmpty(stringId) && idByStringId.TryGetValue(stringId, out id))
                return true;

            id = AirId;
            return false;
        }

        /// <summary>Full definition for an ID; null for air or unknown values.</summary>
        public BlockDefinition GetDefinition(ushort id)
        {
            return id > 0 && id < definitionsById.Count ? definitionsById[id] : null;
        }

        public bool IsTransparent(ushort id)
        {
            return id >= transparentById.Length || transparentById[id];
        }

        public bool IsSolid(ushort id)
        {
            return id < solidById.Length && solidById[id];
        }
    }
}
