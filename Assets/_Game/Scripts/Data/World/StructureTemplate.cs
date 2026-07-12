using System;
using System.Collections.Generic;
using IslandGame.Data.Building;
using IslandGame.Data.Creatures;
using UnityEngine;

namespace IslandGame.Data.World
{
    /// <summary>One building piece of a structure layout, in structure-local space.</summary>
    [Serializable]
    public sealed class StructurePieceEntry
    {
        [Tooltip("The building piece placed here — REAL placed pieces (registry, durability, deconstruction, future saving), exactly like player builds.")]
        public BuildingPieceDefinition piece;

        public Vector3 localPosition;

        [Tooltip("Yaw around Y in structure-local space, degrees (pieces are 2 m grid — usually multiples of 90).")]
        public float localYawDegrees;

        [Tooltip("Seeded chance this piece is MISSING at placement — the whole 'ruined' look: author the intact layout, let omission wreck it differently per site. 0 = always present.")]
        [Range(0f, 1f)]
        public float omitChance;
    }

    /// <summary>
    /// One loot chest of a structure: a chest-type piece plus its fill table.
    /// The table REUSES the creature phase's LootTableEntry shape (item +
    /// count range + chance) — one loot format across the whole game.
    /// </summary>
    [Serializable]
    public sealed class StructureChestEntry
    {
        [Tooltip("The chest piece placed (any piece whose prefab carries a ChestBehavior).")]
        public BuildingPieceDefinition chestPiece;

        public Vector3 localPosition;

        public float localYawDegrees;

        [Tooltip("Rolled once per chest at placement (seeded): chance per line, then count range, straight into the chest's storage.")]
        public List<LootTableEntry> loot = new List<LootTableEntry>();
    }

    /// <summary>Optional creature guard: a CreatureSpawner configured at placement ('hostiles nesting in the ruin').</summary>
    [Serializable]
    public sealed class StructureSpawnerEntry
    {
        public CreatureDefinition creature;

        [Tooltip("Spawner position in structure-local space.")]
        public Vector3 localOffset;

        [Range(1, 10)]
        public int maxPopulation = 2;

        [Min(2f)]
        public float spawnRadius = 6f;

        [Tooltip("Guards only muster after dark (uses the spawner's day/night event hookup).")]
        public bool spawnOnlyAtNight;
    }

    /// <summary>
    /// Authored data for one procedurally-placed structure (ruin, dock,
    /// camp): a layout of existing BuildingPieceDefinitions with per-piece
    /// omission chances (the ruin effect), loot chests and optional creature
    /// guards. Standard pattern: stable ID + SO + StructureTemplateDatabase,
    /// auto-synced. The placement half lives in StructurePlacementSystem,
    /// which validates sites against the world generator's height data.
    /// </summary>
    [CreateAssetMenu(fileName = "NewStructure", menuName = "Island Game/Structure Template")]
    public sealed class StructureTemplate : ScriptableObject, IDefinition
    {
        [Header("Identity")]
        [Tooltip("Stable unique ID (lowercase_underscore). Serialized into world-save data later — never change it once used. Auto-filled from the asset name when empty.")]
        [SerializeField] private string id;

        [SerializeField] private string displayName;

        [TextArea(2, 4)]
        [SerializeField] private string description;

        [Header("Layout")]
        [SerializeField] private List<StructurePieceEntry> pieces = new List<StructurePieceEntry>();

        [SerializeField] private List<StructureChestEntry> chests = new List<StructureChestEntry>();

        [SerializeField] private List<StructureSpawnerEntry> spawners = new List<StructureSpawnerEntry>();

        [Header("Placement")]
        [Tooltip("Ground type this structure needs — Inland (flat grass) or Coast (shore with open water off one side).")]
        [SerializeField] private StructureSurface surface = StructureSurface.Inland;

        [Tooltip("Radius of the footprint validated for flatness, meters.")]
        [Min(2f)]
        [SerializeField] private float footprintRadius = 6f;

        [Tooltip("Maximum terrain height difference across the footprint, blocks.")]
        [Range(0, 6)]
        [SerializeField] private int maxHeightVariance = 2;

        [Tooltip("Relative pick weight when several templates fit a site.")]
        [Min(0.01f)]
        [SerializeField] private float spawnWeight = 1f;

        [Tooltip("Coast only: how far seaward (local +Z) the layout extends — placement requires open water at this distance and aims the structure there.")]
        [Min(2f)]
        [SerializeField] private float coastSeawardExtent = 8f;

        [Tooltip("Coast only: structure origin height above sea level (deck clearance over the water).")]
        [Range(0f, 2f)]
        [SerializeField] private float coastOriginAboveSea = 0.6f;

        public string Id => id;
        public string DisplayName => displayName;
        public string Description => description;
        public IReadOnlyList<StructurePieceEntry> Pieces => pieces;
        public IReadOnlyList<StructureChestEntry> Chests => chests;
        public IReadOnlyList<StructureSpawnerEntry> Spawners => spawners;
        public StructureSurface Surface => surface;
        public float FootprintRadius => footprintRadius;
        public int MaxHeightVariance => maxHeightVariance;
        public float SpawnWeight => spawnWeight;
        public float CoastSeawardExtent => coastSeawardExtent;
        public float CoastOriginAboveSea => coastOriginAboveSea;

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
