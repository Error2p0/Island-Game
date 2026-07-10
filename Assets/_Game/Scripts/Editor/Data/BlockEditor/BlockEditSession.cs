using IslandGame.Data.Blocks;
using UnityEditor;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Edit buffer for one BlockDefinition in the Block Editor window. All
    /// field edits go through the SerializedObject and stay UNAPPLIED until
    /// Save() — that is what gives the window real Save/Revert semantics
    /// instead of Unity's usual apply-as-you-type. The asset on disk is only
    /// touched by Save().
    ///
    /// Never call Serialized.Update()/UpdateIfRequiredOrScript() on this
    /// buffer: they re-read the asset and silently discard pending edits.
    /// </summary>
    internal sealed class BlockEditSession
    {
        public BlockDefinition Target { get; }

        public SerializedObject Serialized { get; private set; }

        public BlockEditSession(BlockDefinition target)
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

                // The ID may have changed; keep anything holding the database honest.
                var database = Resources.Load<BlockDatabase>(BlockDatabase.ResourcesPath);
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

        /// <summary>
        /// Buffered mirror of BlockDefinition.GetFaceTexture (uniform toggle,
        /// per-face with fallback), so the cube preview and thumbnails show
        /// UNSAVED texture assignments live.
        /// </summary>
        public Texture2D GetFaceTexture(BlockFace face)
        {
            var uniformTexture = Serialized.FindProperty("uniformTexture").objectReferenceValue as Texture2D;

            if (Serialized.FindProperty("useUniformTexture").boolValue)
                return uniformTexture;

            var perFace = Serialized.FindProperty(FacePropertyName(face)).objectReferenceValue as Texture2D;
            return perFace != null ? perFace : uniformTexture;
        }

        /// <summary>Serialized field name backing one face slot on BlockDefinition.</summary>
        public static string FacePropertyName(BlockFace face)
        {
            switch (face)
            {
                case BlockFace.Top: return "topTexture";
                case BlockFace.Bottom: return "bottomTexture";
                case BlockFace.North: return "northTexture";
                case BlockFace.South: return "southTexture";
                case BlockFace.East: return "eastTexture";
                default: return "westTexture";
            }
        }
    }
}
