using UnityEngine;

namespace IslandGame.Terrain
{
    /// <summary>
    /// The fine-detail payload of ONE promoted (damaged) block cell: a
    /// resolution³ grid of filled/empty sub-cells. The block's material stays
    /// whatever its base palette ID says — sub-cells only record shape, which
    /// is what "Teardown-style local destruction" needs and keeps the payload
    /// a pure bitset.
    ///
    /// STORAGE: ulong[] bitset, not bool[] — 8³ = 512 cells in 64 bytes
    /// instead of 512, one contiguous allocation, and the maintained
    /// FilledCount makes the demotion check (IsEmpty) and the mesher's
    /// boundary-layer queries cheap. At the default resolution 8 a promoted
    /// cell costs ~64 B payload + ~48 B object header + ~40 B dictionary
    /// entry ≈ 150 B, versus 2 B for a pristine block — fine, because only
    /// damaged cells are ever promoted.
    ///
    /// Resolution is per-grid (captured at creation from the world setting),
    /// so changing the setting mid-session never corrupts existing grids —
    /// the mesher reads each grid's own resolution.
    /// </summary>
    public sealed class SubVoxelGrid
    {
        private readonly ulong[] bits;

        private SubVoxelGrid(int resolution)
        {
            Resolution = resolution;
            int cellCount = resolution * resolution * resolution;
            bits = new ulong[(cellCount + 63) >> 6];
        }

        /// <summary>Sub-cells per block axis (2..16; the world setting picks it at promotion time).</summary>
        public int Resolution { get; }

        /// <summary>Number of filled sub-cells. 0 ⇒ the owning block cell demotes to air.</summary>
        public int FilledCount { get; private set; }

        public bool IsEmpty => FilledCount == 0;

        public bool IsFull => FilledCount == Resolution * Resolution * Resolution;

        /// <summary>A fully-filled grid — the promotion state, visually identical to the intact block.</summary>
        public static SubVoxelGrid CreateFilled(int resolution)
        {
            resolution = Mathf.Clamp(resolution, 2, 16);
            var grid = new SubVoxelGrid(resolution);

            int cellCount = resolution * resolution * resolution;
            for (int i = 0; i < grid.bits.Length; i++)
                grid.bits[i] = ulong.MaxValue;

            // Mask off the bits past the last cell so popcounts stay honest.
            int remainder = cellCount & 63;
            if (remainder != 0)
                grid.bits[grid.bits.Length - 1] = (1UL << remainder) - 1UL;

            grid.FilledCount = cellCount;
            return grid;
        }

        public bool Get(int x, int y, int z)
        {
            int index = Index(x, y, z);
            return (bits[index >> 6] & (1UL << (index & 63))) != 0;
        }

        /// <summary>Sets one sub-cell. Returns true when the value actually changed.</summary>
        public bool Set(int x, int y, int z, bool filled)
        {
            int index = Index(x, y, z);
            ulong mask = 1UL << (index & 63);
            bool current = (bits[index >> 6] & mask) != 0;
            if (current == filled)
                return false;

            if (filled)
            {
                bits[index >> 6] |= mask;
                FilledCount++;
            }
            else
            {
                bits[index >> 6] &= ~mask;
                FilledCount--;
            }

            return true;
        }

        /// <summary>
        /// True when the boundary layer of sub-cells on the given face
        /// (BlockFace order: Top, Bottom, North, South, East, West) is
        /// completely filled — the mesher then treats this promoted cell as a
        /// solid wall from that side and culls the neighbor's big face against
        /// it, exactly like an intact block. Any hole ⇒ the neighbor draws its
        /// full face behind the detail (cheap, z-buffered) so carving can
        /// never open a see-through gap.
        /// </summary>
        public bool IsFaceLayerFull(int face)
        {
            int res = Resolution;

            for (int a = 0; a < res; a++)
            {
                for (int b = 0; b < res; b++)
                {
                    bool filled;
                    switch (face)
                    {
                        case 0: filled = Get(a, res - 1, b); break; // Top    (+Y)
                        case 1: filled = Get(a, 0, b); break;       // Bottom (−Y)
                        case 2: filled = Get(a, b, res - 1); break; // North  (+Z)
                        case 3: filled = Get(a, b, 0); break;       // South  (−Z)
                        case 4: filled = Get(res - 1, a, b); break; // East   (+X)
                        default: filled = Get(0, a, b); break;      // West   (−X)
                    }

                    if (!filled)
                        return false;
                }
            }

            return true;
        }

        // ------------------------------------------------------------------
        // Save phase
        // ------------------------------------------------------------------

        /// <summary>The raw bitset as bytes (little-endian ulongs) — the save phase Base64s this.</summary>
        public byte[] ExportBits()
        {
            var data = new byte[bits.Length * 8];
            System.Buffer.BlockCopy(bits, 0, data, 0, data.Length);
            return data;
        }

        /// <summary>
        /// Overwrites the bitset from ExportBits data and recounts FilledCount.
        /// False (grid unchanged) when the payload length doesn't match this
        /// grid's resolution — a corrupt or resolution-mismatched save line.
        /// </summary>
        public bool ImportBits(byte[] data)
        {
            if (data == null || data.Length != bits.Length * 8)
                return false;

            System.Buffer.BlockCopy(data, 0, bits, 0, data.Length);

            int filled = 0;
            for (int i = 0; i < bits.Length; i++)
            {
                ulong value = bits[i];
                while (value != 0)
                {
                    value &= value - 1; // clear lowest set bit
                    filled++;
                }
            }

            FilledCount = filled;
            return true;
        }

        /// <summary>
        /// True when the other grid has the same resolution and identical bits.
        /// The save phase uses this to skip promotions that match the
        /// regenerated baseline — generation-time surface shaping promotes the
        /// whole surface shell, and only player-touched grids are worth saving.
        /// </summary>
        public bool ContentEquals(SubVoxelGrid other)
        {
            if (other == null || other.Resolution != Resolution || other.FilledCount != FilledCount)
                return false;

            for (int i = 0; i < bits.Length; i++)
            {
                if (bits[i] != other.bits[i])
                    return false;
            }

            return true;
        }

        // Y-major like Chunk: consecutive x is consecutive bits.
        private int Index(int x, int y, int z)
        {
            return (y * Resolution + z) * Resolution + x;
        }
    }
}
