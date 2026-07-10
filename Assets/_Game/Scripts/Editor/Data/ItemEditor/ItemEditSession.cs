using IslandGame.Data.Items;
using UnityEditor;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Edit buffer for one ItemDefinition in the Item Editor window — same
    /// model as the Block Editor's BlockEditSession: all edits stay in an
    /// UNAPPLIED SerializedObject until Save(), giving real Save/Revert
    /// semantics. The asset on disk is only touched by Save().
    ///
    /// Never call Serialized.Update()/UpdateIfRequiredOrScript() on this
    /// buffer: they re-read the asset and silently discard pending edits.
    /// </summary>
    internal sealed class ItemEditSession
    {
        public ItemDefinition Target { get; }

        public SerializedObject Serialized { get; private set; }

        public ItemEditSession(ItemDefinition target)
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

        // Buffered reads for the live preview and validation — all reflect
        // unsaved edits, not the on-disk asset.

        public GameObject CurrentWorldModelPrefab =>
            Serialized.FindProperty("worldModelPrefab").objectReferenceValue as GameObject;

        public HoldType CurrentHoldType => (HoldType)Serialized.FindProperty("holdType").intValue;

        public HoldSocket CurrentHoldSocket => (HoldSocket)Serialized.FindProperty("holdSocket").intValue;

        public Vector3 CurrentHoldLocalPosition => Serialized.FindProperty("holdLocalPosition").vector3Value;

        public Vector3 CurrentHoldLocalRotationEuler =>
            Serialized.FindProperty("holdLocalRotationEuler").vector3Value;

        /// <summary>Writes pending edits to the asset on disk and refreshes runtime lookups.</summary>
        public void Save()
        {
            if (!IsValid)
                return;

            if (Serialized.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(Target);
                AssetDatabase.SaveAssetIfDirty(Target);

                // The ID may have changed; keep anything holding the database honest.
                var database = Resources.Load<ItemDatabase>(ItemDatabase.ResourcesPath);
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
