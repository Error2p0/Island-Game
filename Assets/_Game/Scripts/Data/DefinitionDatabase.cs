using System.Collections.Generic;
using UnityEngine;

namespace IslandGame.Data
{
    /// <summary>
    /// Base for the ItemDatabase/BlockDatabase registry assets: the single
    /// runtime lookup point for definitions by ID, used by inventory, terrain,
    /// crafting, the creative menu and the editor tools.
    ///
    /// The serialized list is populated by the editor-side sync utility
    /// (Island Game/Data/Sync Databases — also runs automatically when
    /// definition assets are imported/deleted/moved). It is never hand-edited;
    /// hand edits are overwritten on the next sync.
    /// </summary>
    public abstract class DefinitionDatabase<TDefinition> : ScriptableObject
        where TDefinition : ScriptableObject, IDefinition
    {
        [Tooltip("Managed by Island Game/Data/Sync Databases — do not edit by hand.")]
        [SerializeField] private List<TDefinition> definitions = new List<TDefinition>();

        private Dictionary<string, TDefinition> byId;

        /// <summary>Every definition in the project, sorted by ID (sync order).</summary>
        public IReadOnlyList<TDefinition> All => definitions;

        public int Count => definitions.Count;

        /// <summary>ID lookup without error logging — use when a miss is an expected case.</summary>
        public bool TryGet(string id, out TDefinition definition)
        {
            EnsureLookup();

            if (!string.IsNullOrEmpty(id) && byId.TryGetValue(id, out definition))
                return true;

            definition = null;
            return false;
        }

        /// <summary>
        /// ID lookup that logs a clear error and returns null on a miss — use when
        /// the ID comes from serialized data that should always resolve (terrain
        /// saves, inventory saves, recipes).
        /// </summary>
        public TDefinition Get(string id)
        {
            if (TryGet(id, out TDefinition definition))
                return definition;

            Debug.LogError(
                $"[{name}] No definition with ID '{id}'. The asset was renamed-by-ID, deleted, " +
                "or the database is out of sync (run Island Game/Data/Sync Databases).", this);
            return null;
        }

        public bool Contains(string id)
        {
            return TryGet(id, out _);
        }

        /// <summary>
        /// Full integrity check, shared by the runtime lookup build and the editor
        /// tools (sync utility now, the Phase 2/3 editors later). Returns one
        /// human-readable message per problem; empty list means healthy.
        /// </summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            var seen = new Dictionary<string, TDefinition>(definitions.Count);

            for (int i = 0; i < definitions.Count; i++)
            {
                TDefinition definition = definitions[i];

                if (definition == null)
                {
                    errors.Add($"Entry {i} is missing (deleted asset?). Re-run Island Game/Data/Sync Databases.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(definition.Id))
                {
                    errors.Add($"'{definition.name}' has an empty ID. Every definition needs a stable unique ID.");
                    continue;
                }

                if (seen.TryGetValue(definition.Id, out TDefinition existing))
                {
                    errors.Add(
                        $"Duplicate ID '{definition.Id}' on '{existing.name}' and '{definition.name}'. " +
                        "Lookups resolve to the first — change the ID on one of them.");
                    continue;
                }

                seen.Add(definition.Id, definition);
            }

            return errors;
        }

        /// <summary>
        /// Rebuilds the ID dictionary. Called lazily on first lookup; editor tools
        /// call it after changing IDs so stale mappings never survive.
        /// </summary>
        public void RebuildLookup()
        {
            byId = new Dictionary<string, TDefinition>(definitions.Count);

            foreach (TDefinition definition in definitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
                    continue; // Validate() reports these with context; skip so lookups still work.

                if (byId.ContainsKey(definition.Id))
                {
                    Debug.LogError(
                        $"[{name}] Duplicate ID '{definition.Id}' — '{definition.name}' is shadowed by " +
                        $"'{byId[definition.Id].name}'. Fix the ID on one of them.", definition);
                    continue;
                }

                byId.Add(definition.Id, definition);
            }
        }

        private void EnsureLookup()
        {
            if (byId == null)
                RebuildLookup();
        }

        private void OnEnable()
        {
            // Drop the cache across domain reloads / play-mode transitions so a
            // freshly-synced list can never be paired with a stale dictionary.
            byId = null;
        }

#if UNITY_EDITOR
        /// <summary>Editor-only entry point for the sync utility. Replaces the list and invalidates the lookup.</summary>
        public void EditorSetDefinitions(IEnumerable<TDefinition> newDefinitions)
        {
            definitions = new List<TDefinition>(newDefinitions);
            byId = null;
        }
#endif
    }
}
