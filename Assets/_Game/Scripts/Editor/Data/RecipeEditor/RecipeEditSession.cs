using IslandGame.Data.Crafting;
using UnityEditor;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Edit buffer for one RecipeDefinition in the Recipe Editor — same model
    /// as the Block/Item editors: edits stay in an UNAPPLIED SerializedObject
    /// until Save(), so Save/Revert are real. Never call Update()/
    /// UpdateIfRequiredOrScript() on the buffer: pending edits would be lost.
    /// </summary>
    internal sealed class RecipeEditSession
    {
        public RecipeDefinition Target { get; }

        public SerializedObject Serialized { get; private set; }

        public RecipeEditSession(RecipeDefinition target)
        {
            Target = target;
            Serialized = new SerializedObject(target);
        }

        /// <summary>False once the target asset was deleted out from under the session.</summary>
        public bool IsValid => Target != null;

        public bool HasUnsavedChanges => IsValid && Serialized.hasModifiedProperties;

        /// <summary>The ID as currently edited (possibly unsaved) — for live collision warnings.</summary>
        public string CurrentId => Serialized.FindProperty("id").stringValue;

        public string CurrentDisplayName
        {
            get
            {
                string displayName = Serialized.FindProperty("displayName").stringValue;
                return string.IsNullOrEmpty(displayName) ? Target.name : displayName;
            }
        }

        /// <summary>Writes pending edits to the asset on disk and refreshes runtime lookups.</summary>
        public void Save()
        {
            if (!IsValid)
                return;

            if (Serialized.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(Target);
                AssetDatabase.SaveAssetIfDirty(Target);

                var database = Resources.Load<RecipeDatabase>(RecipeDatabase.ResourcesPath);
                if (database != null)
                    database.RebuildLookup();
            }
        }

        /// <summary>Discards pending edits, returning the buffer to the on-disk state.</summary>
        public void Revert()
        {
            if (!IsValid)
                return;

            Serialized.Dispose();
            Serialized = new SerializedObject(Target);
        }
    }
}
