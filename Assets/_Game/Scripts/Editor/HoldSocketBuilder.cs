using IslandGame.Combat;
using IslandGame.Data.Items;
using IslandGame.Held;
using IslandGame.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IslandGame.EditorTools
{
    /// <summary>
    /// One-click Phase 9 rig setup: creates the hold socket transforms on the
    /// player's hand/chest bones per HoldSocketConvention (resolved through the
    /// humanoid avatar, so bone names don't matter), ensures ItemHoldController
    /// and PlayerWeaponAttack on the player, and wires the socket references.
    /// Idempotent — existing sockets are reused, and their local offsets (palm
    /// fit) are yours to tune in the scene afterwards.
    /// </summary>
    public static class HoldSocketBuilder
    {
        [MenuItem("Island Game/Player/Create Hold Sockets")]
        public static void Create()
        {
            var playerReferences = Object.FindFirstObjectByType<PlayerReferences>();
            if (playerReferences == null)
            {
                Debug.LogError("No PlayerReferences found in the open scene — open the gameplay scene with the player first.");
                return;
            }

            var animator = playerReferences.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                Debug.LogError("Player has no humanoid Animator/avatar — run the rig builder first.");
                return;
            }

            Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            if (chest == null)
                chest = animator.GetBoneTransform(HumanBodyBones.Spine);

            if (rightHand == null || leftHand == null || chest == null)
            {
                Debug.LogError("Could not resolve hand/chest bones from the avatar.");
                return;
            }

            Transform rightSocket = EnsureSocket(rightHand, HoldSocketConvention.RightHandSocketName, new Vector3(0.08f, 0f, 0f));
            Transform leftSocket = EnsureSocket(leftHand, HoldSocketConvention.LeftHandSocketName, new Vector3(-0.08f, 0f, 0f));
            Transform twoHandedSocket = EnsureSocket(chest, HoldSocketConvention.TwoHandedSocketName, new Vector3(0f, 0.1f, 0.35f));

            var holdController = playerReferences.GetComponent<ItemHoldController>();
            if (holdController == null)
            {
                holdController = Undo.AddComponent<ItemHoldController>(playerReferences.gameObject);
                Debug.Log($"Added ItemHoldController to '{playerReferences.name}'.", playerReferences);
            }

            if (playerReferences.GetComponent<PlayerWeaponAttack>() == null)
            {
                Undo.AddComponent<PlayerWeaponAttack>(playerReferences.gameObject);
                Debug.Log($"Added PlayerWeaponAttack to '{playerReferences.name}'.", playerReferences);
            }

            var serialized = new SerializedObject(holdController);
            serialized.FindProperty("rightHandSocket").objectReferenceValue = rightSocket;
            serialized.FindProperty("leftHandSocket").objectReferenceValue = leftSocket;
            serialized.FindProperty("twoHandedSocket").objectReferenceValue = twoHandedSocket;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(playerReferences.gameObject.scene);
            Selection.activeGameObject = playerReferences.gameObject;
            Debug.Log(
                "Hold sockets ready (Socket_RightHand / Socket_LeftHand / Socket_TwoHanded) and components wired. " +
                "REMINDER: rerun Tools/Island Game/Build Player Animations && Controller to regenerate the Animator " +
                "with the hold poses, and tune socket offsets in the scene if items sit oddly in the palm.");
        }

        private static Transform EnsureSocket(Transform bone, string socketName, Vector3 localPosition)
        {
            Transform existing = bone.Find(socketName);
            if (existing != null)
                return existing;

            var socket = new GameObject(socketName).transform;
            Undo.RegisterCreatedObjectUndo(socket.gameObject, "Create Hold Sockets");
            socket.SetParent(bone, false);
            socket.localPosition = localPosition;
            socket.localRotation = Quaternion.identity;
            Debug.Log($"Created '{socketName}' under '{bone.name}'.");
            return socket;
        }
    }
}
