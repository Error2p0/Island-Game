using System.Collections.Generic;
using IslandGame.Data.Building;
using UnityEditor;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Snapshot of one socket as currently edited (possibly unsaved), handed
    /// to the 3D preview so gizmos always show the buffer, not the asset.
    /// </summary>
    internal readonly struct SocketPreviewData
    {
        public readonly string Name;
        public readonly Vector3 LocalPosition;
        public readonly Vector3 LocalRotationEuler;
        public readonly string Tag;

        public SocketPreviewData(string name, Vector3 localPosition, Vector3 localRotationEuler, string tag)
        {
            Name = name;
            LocalPosition = localPosition;
            LocalRotationEuler = localRotationEuler;
            Tag = tag;
        }
    }

    /// <summary>
    /// Edit buffer for one BuildingPieceDefinition in the Building Piece
    /// Editor window. All field edits go through the SerializedObject and
    /// stay UNAPPLIED until Save() — same Save/Revert semantics as the
    /// Block/Item/Recipe edit sessions. The asset on disk is only touched by
    /// Save().
    ///
    /// Never call Serialized.Update()/UpdateIfRequiredOrScript() on this
    /// buffer: they re-read the asset and silently discard pending edits.
    /// </summary>
    internal sealed class BuildingPieceEditSession
    {
        public BuildingPieceDefinition Target { get; }

        public SerializedObject Serialized { get; private set; }

        public BuildingPieceEditSession(BuildingPieceDefinition target)
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

        /// <summary>The prefab as currently edited (possibly unsaved) — the preview renders this.</summary>
        public GameObject CurrentPrefab => Serialized.FindProperty("prefab").objectReferenceValue as GameObject;

        public int SocketCount => Serialized.FindProperty("sockets").arraySize;

        /// <summary>Buffered socket values for the preview gizmos (unsaved edits show live).</summary>
        public void GetSockets(List<SocketPreviewData> buffer)
        {
            buffer.Clear();
            SerializedProperty sockets = Serialized.FindProperty("sockets");

            for (int i = 0; i < sockets.arraySize; i++)
            {
                SerializedProperty socket = sockets.GetArrayElementAtIndex(i);
                buffer.Add(new SocketPreviewData(
                    socket.FindPropertyRelative("socketName").stringValue,
                    socket.FindPropertyRelative("localPosition").vector3Value,
                    socket.FindPropertyRelative("localRotationEuler").vector3Value,
                    socket.FindPropertyRelative("tag").stringValue));
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
                var database = Resources.Load<BuildingPieceDatabase>(BuildingPieceDatabase.ResourcesPath);
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
