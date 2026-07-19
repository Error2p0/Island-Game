using System;
using System.Collections.Generic;
using UnityEngine;

namespace IslandGame.Saving
{
    /// <summary>
    /// The complete on-disk save format — one JSON document (JsonUtility).
    ///
    /// FORMAT CHOICE — JSON over binary, deliberately:
    ///   • The delta model (below) keeps payloads small: a heavy session is
    ///     hundreds of edited cells + dozens of promotions + pieces — tens of
    ///     KB of JSON, where binary would only shine if we stored full 32 KB
    ///     chunks, which the delta model exists to avoid.
    ///   • JsonUtility gives the required version resilience FOR FREE:
    ///     unknown JSON fields are ignored on read, missing fields default —
    ///     additive format changes never hard-break old saves.
    ///   • Debuggability: a save you can open in a text editor is worth real
    ///     money during the next five phases.
    ///   • Escape hatch if worlds ever grow to MBs: GZip the file — the class
    ///     tree doesn't change.
    ///   Sub-voxel bitsets (the only binary-ish payload) ride as Base64.
    ///
    /// REFERENCES are stable string IDs everywhere (items, blocks, pieces,
    /// stats) — never asset references or session-local numeric palette ids,
    /// per the project's Phase 1 rule. Block ids use a per-chunk string table
    /// so a hundred dirt edits don't repeat "dirt" a hundred times.
    ///
    /// VERSIONING: `version` is written on save; loads warn (but still
    /// attempt, thanks to the ignore-unknown semantics) when a file is NEWER
    /// than the running code. Additive fields must always come with sensible
    /// defaults — that is the entire migration policy, by design.
    /// </summary>
    [Serializable]
    public sealed class SaveGame
    {
        /// <summary>Bump when the format changes shape (additive changes usually don't need it).</summary>
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public string savedAtUtc;

        // World identity + clock.
        public int worldSeed;
        public float timeOfDay01;
        public int dayNumber;

        public SavedPlayer player = new SavedPlayer();
        public List<SavedChunkDelta> terrain = new List<SavedChunkDelta>();
        public List<SavedPiece> pieces = new List<SavedPiece>();
        public List<SavedStructureCell> structureCells = new List<SavedStructureCell>();

        // Structure guard spawners are runtime objects the placement system
        // created; restored processed cells suppress re-placement, so their
        // configs persist explicitly.
        public List<SavedGuardSpawner> guardSpawners = new List<SavedGuardSpawner>();

        // Creatures are deliberately NOT persisted — see SaveManager's
        // creature policy note. This list exists so a future taming phase can
        // add named/owned creatures without a format bump.
        public List<SavedCreature> ownedCreatures = new List<SavedCreature>();
    }

    [Serializable]
    public sealed class SavedPlayer
    {
        public Vector3 position;
        public float yaw;
        public float pitch;
        public int selectedHotbarIndex;
        public List<SavedItemSlot> inventory = new List<SavedItemSlot>();
        public List<SavedStat> stats = new List<SavedStat>();

        // Death phase (additive with defaults, per the migration policy):
        // the bed respawn point. false = world spawn, exactly the pre-death-
        // phase behavior every old save deserializes to. The gravestone
        // itself needs nothing here — it's an ordinary registered piece with
        // a chest, already covered by SavedPiece.
        public bool hasRespawnPoint;
        public Vector3 respawnPosition;
        public float respawnYaw;
    }

    /// <summary>One inventory slot: stable item ID (empty string = empty slot), count, and the Phase 2 durability value.</summary>
    [Serializable]
    public sealed class SavedItemSlot
    {
        public string itemId = string.Empty;
        public int count;
        public float durability01 = 1f;
    }

    [Serializable]
    public sealed class SavedStat
    {
        public string statId;
        public float current;
    }

    /// <summary>
    /// One modified chunk's changes from its regenerated baseline: parallel
    /// arrays of flattened local cell index → index into the per-chunk block
    /// id string table, plus every promoted (sub-voxel damaged) cell.
    /// </summary>
    [Serializable]
    public sealed class SavedChunkDelta
    {
        public int coordX;
        public int coordZ;
        public List<int> cellIndices = new List<int>();
        public List<int> blockIdTableIndices = new List<int>();
        public List<string> blockIdTable = new List<string>();
        public List<SavedPromotedCell> promotions = new List<SavedPromotedCell>();
    }

    [Serializable]
    public sealed class SavedPromotedCell
    {
        public int cellIndex;
        public int resolution;
        public string bitsBase64;
    }

    /// <summary>
    /// One placed building piece (player-built and structure-spawned alike —
    /// they share the registry). Functional payloads are optional additive
    /// fields: absent/default values simply mean "not that kind of piece".
    /// </summary>
    [Serializable]
    public sealed class SavedPiece
    {
        public string pieceId;
        public Vector3 position;
        public Quaternion rotation;
        public float health;

        // Campfire (fuel < 0 = no campfire state saved).
        public float campfireFuel = -1f;
        public bool campfireLit;

        // Door.
        public bool hasDoorState;
        public bool doorOpen;

        // Chest.
        public bool hasChest;
        public List<SavedItemSlot> chestSlots = new List<SavedItemSlot>();
    }

    [Serializable]
    public sealed class SavedStructureCell
    {
        public int x;
        public int z;
    }

    /// <summary>A structure's creature nest: the spawner config, by stable creature ID.</summary>
    [Serializable]
    public sealed class SavedGuardSpawner
    {
        public string creatureId;
        public Vector3 position;
        public int maxPopulation = 2;
        public float spawnRadius = 6f;
        public bool spawnOnlyAtNight;
    }

    /// <summary>
    /// One tamed companion (taming phase — the case this class was reserved
    /// for). WILD creatures still intentionally don't persist (spawners
    /// repopulate them); only the player's named individuals are written.
    /// mode/yaw/stats are additive fields with defaults, per the migration
    /// policy; health is kept alongside the full stat list for forward/
    /// backward tolerance.
    /// </summary>
    [Serializable]
    public sealed class SavedCreature
    {
        public string creatureId;
        public Vector3 position;
        public float health;
        public string customName;

        // Taming phase additions.
        public float yaw;
        public string mode = "Follow";
        public List<SavedStat> stats = new List<SavedStat>();
    }
}
