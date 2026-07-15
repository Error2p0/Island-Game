using System;
using System.Collections.Generic;
using IslandGame.Data.Blocks;
using UnityEngine;

namespace IslandGame.Data.World
{
    /// <summary>Which material a tree stroke rasterizes as. Trunk and Branch are both the trunk block — separate labels for authoring clarity (and future per-part rules).</summary>
    public enum TreePart
    {
        Trunk = 0,
        Branch = 1,
        Leaves = 2,
    }

    /// <summary>
    /// One shape primitive of a tree: a capsule from Start to End with Radius,
    /// in TREE-LOCAL METERS (origin = trunk base center on the ground, +Y up).
    /// Start == End makes a sphere — the usual leaf-cluster form. The
    /// rasterizer fills every sub-voxel whose center lies inside.
    /// </summary>
    [Serializable]
    public sealed class TreeStroke
    {
        [SerializeField] private TreePart part = TreePart.Trunk;
        [SerializeField] private Vector3 start;
        [SerializeField] private Vector3 end;

        [Min(0.05f)]
        [SerializeField] private float radius = 0.25f;

        public TreeStroke()
        {
        }

        public TreeStroke(TreePart part, Vector3 start, Vector3 end, float radius)
        {
            this.part = part;
            this.start = start;
            this.end = end;
            this.radius = radius;
        }

        public TreePart Part => part;
        public Vector3 Start => start;
        public Vector3 End => end;
        public float Radius => radius;
    }

    /// <summary>
    /// Authored data for one tree variety: a compact stroke program (capsule
    /// trunk/branches + sphere leaf clusters) that the world generator
    /// rasterizes into voxels at chunk-generation time. Strokes were chosen
    /// over raw sub-cell arrays (unauthorable by hand, resolution-fragile)
    /// and over a runtime L-system (this keeps determinism trivial and the
    /// data inspectable) — a handful of floats fully describes a tree at any
    /// resolution.
    ///
    /// COST MODEL: cells fully inside the shape become PLAIN BASE BLOCKS
    /// (2 bytes, fast mesh path); only boundary/partial cells promote to
    /// sub-voxel grids, at THIS template's Resolution — deliberately lower
    /// (default 4 → 8-byte bitsets, 64-iteration mesh sweeps) than the
    /// terrain-mining resolution, which is the scale mitigation that lets
    /// every tree exist fully materialized with no distance-based promotion
    /// machinery. Phase 4's per-grid resolution + proportional seam mapping
    /// makes res-4 tree cells and res-8 mining bites coexist in one block.
    ///
    /// Trunk/leaf materials are real BlockDefinitions (drops, hardness,
    /// transparency, NeedsSupport flag all come from them) — trees are pure
    /// content on top of the existing block/mining systems.
    /// </summary>
    [CreateAssetMenu(fileName = "NewTreeTemplate", menuName = "Island Game/Tree Template")]
    public sealed class TreeTemplateDefinition : ScriptableObject, IDefinition
    {
        [Header("Identity")]
        [Tooltip("Stable unique ID (lowercase_underscore). Auto-filled from the asset name when empty; never change it once worlds reference it.")]
        [SerializeField] private string id;

        [Tooltip("Name shown in tooling.")]
        [SerializeField] private string displayName;

        [Header("Materials")]
        [Tooltip("Block for Trunk/Branch strokes (should carry NeedsSupport, drop a log-style item).")]
        [SerializeField] private BlockDefinition trunkBlock;

        [Tooltip("Block for Leaves strokes (transparent-flagged BlockDefinition; should carry NeedsSupport).")]
        [SerializeField] private BlockDefinition leavesBlock;

        [Header("Detail")]
        [Tooltip("Sub-cells per block axis for this tree's PARTIAL cells. Lower than the mining resolution on purpose — 4 is the cost sweet spot (see class summary).")]
        [Range(2, 8)]
        [SerializeField] private int resolution = 4;

        [Header("Scattering")]
        [Tooltip("Relative pick weight when the world generator chooses a template for an anchor. 0 = never auto-scattered.")]
        [Min(0f)]
        [SerializeField] private float spawnWeight = 1f;

        [Tooltip("Biome band (foliage phase): the anchor's surface must sit at least this many blocks ABOVE SEA LEVEL. 0 = no lower bound. Grass is required regardless (the tree rule), so values below the beach band change nothing.")]
        [Min(0)]
        [SerializeField] private int minAltitudeAboveSea;

        [Tooltip("Biome band (foliage phase): the anchor's surface must sit at most this many blocks above sea level. 0 = NO UPPER LIMIT (the project's 0-sentinel convention — existing templates deserialize to 0 and keep their everywhere behavior). Example: willow 3 hugs the coast; dead trees pair a high Min with 0 here.")]
        [Min(0)]
        [SerializeField] private int maxAltitudeAboveSea;

        [Header("Shape")]
        [SerializeField] private List<TreeStroke> strokes = new List<TreeStroke>();

        public string Id => id;
        public string DisplayName => displayName;
        public BlockDefinition TrunkBlock => trunkBlock;
        public BlockDefinition LeavesBlock => leavesBlock;
        public int Resolution => resolution;
        public float SpawnWeight => spawnWeight;

        /// <summary>Minimum surface height above sea level to be eligible at an anchor. 0 = no lower bound.</summary>
        public int MinAltitudeAboveSea => minAltitudeAboveSea;

        /// <summary>Maximum surface height above sea level to be eligible at an anchor. 0 = no upper limit (the pre-foliage behavior every existing asset keeps).</summary>
        public int MaxAltitudeAboveSea => maxAltitudeAboveSea;

        /// <summary>The biome-band check the generator's variant pick runs per anchor (pure, so scattering stays a function of the seed).</summary>
        public bool IsEligibleAtAltitude(int altitudeAboveSea)
        {
            if (altitudeAboveSea < minAltitudeAboveSea)
                return false;

            return maxAltitudeAboveSea <= 0 || altitudeAboveSea <= maxAltitudeAboveSea;
        }

        public IReadOnlyList<TreeStroke> Strokes => strokes;

        /// <summary>Largest |x|/|z| any stroke reaches (including radius) — the generator's cross-chunk scan margin.</summary>
        public float MaxHorizontalExtent
        {
            get
            {
                float extent = 0f;
                for (int i = 0; i < strokes.Count; i++)
                {
                    TreeStroke stroke = strokes[i];
                    if (stroke == null)
                        continue;

                    extent = Mathf.Max(extent,
                        Mathf.Abs(stroke.Start.x) + stroke.Radius, Mathf.Abs(stroke.End.x) + stroke.Radius,
                        Mathf.Abs(stroke.Start.z) + stroke.Radius, Mathf.Abs(stroke.End.z) + stroke.Radius);
                }

                return extent;
            }
        }

        /// <summary>Highest point any stroke reaches — the generator skips anchors too close to the world ceiling.</summary>
        public float MaxHeight
        {
            get
            {
                float height = 0f;
                for (int i = 0; i < strokes.Count; i++)
                {
                    TreeStroke stroke = strokes[i];
                    if (stroke == null)
                        continue;

                    height = Mathf.Max(height, stroke.Start.y + stroke.Radius, stroke.End.y + stroke.Radius);
                }

                return height;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Convenience only: a fresh asset inherits its name as ID/display name.
            // An existing ID is never regenerated — stability beats tidiness.
            if (string.IsNullOrWhiteSpace(id) && !string.IsNullOrEmpty(name))
                id = name.Trim().ToLowerInvariant().Replace(' ', '_');
            else if (id != null)
                id = id.Trim();

            if (string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrEmpty(name))
                displayName = name;
        }
#endif
    }
}
