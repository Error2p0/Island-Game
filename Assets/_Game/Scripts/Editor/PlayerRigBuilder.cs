using System.Collections.Generic;
using IslandGame.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace IslandGame.EditorTools
{
    /// <summary>
    /// One-click builder for the Phase 1 placeholder player: a humanoid bone
    /// hierarchy in T-pose with primitive visuals, a baked Humanoid Avatar
    /// (so Phase 6 can retarget real humanoid clips and use built-in IK),
    /// a configured CharacterController, first-person camera, and all core
    /// player components wired together.
    /// </summary>
    public static class PlayerRigBuilder
    {
        private const string PlayerObjectName = "Player";
        private const string AvatarFolder = "Assets/_Game/Player";
        private const string AvatarAssetPath = AvatarFolder + "/PlayerCapsuleAvatar.asset";
        private const string InputAssetPath = "Assets/_Game/Input/PlayerControls.inputactions";

        [MenuItem("Tools/Island Game/Build Player Rig")]
        public static void BuildPlayerRig()
        {
            if (GameObject.Find(PlayerObjectName) != null)
            {
                Debug.LogError($"[PlayerRigBuilder] A GameObject named '{PlayerObjectName}' already exists in the scene. Delete or rename it before rebuilding.");
                return;
            }

            // Avatar baking requires the rig at origin with identity rotation.
            GameObject root = new GameObject(PlayerObjectName);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.tag = "Player";

            BuildSkeletonAndVisuals(root.transform);

            // Bake the avatar BEFORE adding the camera rig so only body
            // transforms end up in the avatar's skeleton definition.
            Animator animator = root.AddComponent<Animator>();
            animator.applyRootMotion = false;
            Avatar avatar = BakeHumanoidAvatar(root);
            if (avatar != null)
                animator.avatar = avatar;

            BuildCameraRig(root.transform);
            AddCharacterController(root);
            AddAndWireComponents(root);

            Undo.RegisterCreatedObjectUndo(root, "Build Player Rig");
            Selection.activeGameObject = root;
            EditorUtility.SetDirty(root);

            WarnAboutOtherCameras(root);
            Debug.Log("[PlayerRigBuilder] Player rig built. Add a ground object (e.g. a Plane at y=0) and press Play to test mouse look.", root);
        }

        // ------------------------------------------------------------------
        // Skeleton + visuals (~1.8 m humanoid, T-pose, bones separate from meshes)
        // ------------------------------------------------------------------

        private static void BuildSkeletonAndVisuals(Transform root)
        {
            // Bones sit at joint positions; identity local rotation everywhere
            // encodes the T-pose the Humanoid Avatar baker expects.
            Transform hips = Bone("Hips", root, new Vector3(0f, 0.95f, 0f));
            Transform spine = Bone("Spine", hips, new Vector3(0f, 1.08f, 0f));
            Transform chest = Bone("Chest", spine, new Vector3(0f, 1.22f, 0f));
            Transform neck = Bone("Neck", chest, new Vector3(0f, 1.50f, 0f));
            Transform head = Bone("Head", neck, new Vector3(0f, 1.58f, 0f));

            Transform leftUpperArm = Bone("LeftUpperArm", chest, new Vector3(-0.21f, 1.45f, 0f));
            Transform leftLowerArm = Bone("LeftLowerArm", leftUpperArm, new Vector3(-0.49f, 1.45f, 0f));
            Transform leftHand = Bone("LeftHand", leftLowerArm, new Vector3(-0.75f, 1.45f, 0f));

            Transform rightUpperArm = Bone("RightUpperArm", chest, new Vector3(0.21f, 1.45f, 0f));
            Transform rightLowerArm = Bone("RightLowerArm", rightUpperArm, new Vector3(0.49f, 1.45f, 0f));
            Transform rightHand = Bone("RightHand", rightLowerArm, new Vector3(0.75f, 1.45f, 0f));

            Transform leftUpperLeg = Bone("LeftUpperLeg", hips, new Vector3(-0.10f, 0.95f, 0f));
            Transform leftLowerLeg = Bone("LeftLowerLeg", leftUpperLeg, new Vector3(-0.10f, 0.52f, 0f));
            Transform leftFoot = Bone("LeftFoot", leftLowerLeg, new Vector3(-0.10f, 0.11f, 0f));

            Transform rightUpperLeg = Bone("RightUpperLeg", hips, new Vector3(0.10f, 0.95f, 0f));
            Transform rightLowerLeg = Bone("RightLowerLeg", rightUpperLeg, new Vector3(0.10f, 0.52f, 0f));
            Transform rightFoot = Bone("RightFoot", rightLowerLeg, new Vector3(0.10f, 0.11f, 0f));

            // Visuals are plain primitives parented under bones. Swapping in a
            // skinned mesh later means deleting these children only.
            Visual(PrimitiveType.Capsule, "PelvisVisual", hips, Vector3.zero, new Vector3(0.32f, 0.12f, 0.24f));
            Visual(PrimitiveType.Capsule, "TorsoVisual", chest, new Vector3(0f, 0.03f, 0f), new Vector3(0.34f, 0.25f, 0.22f));
            Visual(PrimitiveType.Capsule, "NeckVisual", neck, new Vector3(0f, 0.04f, 0f), new Vector3(0.10f, 0.06f, 0.10f));

            // Head is shadows-only: the first-person camera sits inside it and
            // would otherwise see the sphere's interior.
            Transform headVisual = Visual(PrimitiveType.Sphere, "HeadVisual", head, new Vector3(0f, 0.08f, 0f), new Vector3(0.25f, 0.25f, 0.25f));
            headVisual.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.ShadowsOnly;

            Vector3 armRotation = new Vector3(0f, 0f, 90f);
            Visual(PrimitiveType.Capsule, "LeftUpperArmVisual", leftUpperArm, new Vector3(-0.14f, 0f, 0f), new Vector3(0.09f, 0.16f, 0.09f), armRotation);
            Visual(PrimitiveType.Capsule, "LeftForearmVisual", leftLowerArm, new Vector3(-0.13f, 0f, 0f), new Vector3(0.08f, 0.145f, 0.08f), armRotation);
            Visual(PrimitiveType.Cube, "LeftHandVisual", leftHand, new Vector3(-0.06f, 0f, 0f), new Vector3(0.12f, 0.04f, 0.09f));

            Visual(PrimitiveType.Capsule, "RightUpperArmVisual", rightUpperArm, new Vector3(0.14f, 0f, 0f), new Vector3(0.09f, 0.16f, 0.09f), armRotation);
            Visual(PrimitiveType.Capsule, "RightForearmVisual", rightLowerArm, new Vector3(0.13f, 0f, 0f), new Vector3(0.08f, 0.145f, 0.08f), armRotation);
            Visual(PrimitiveType.Cube, "RightHandVisual", rightHand, new Vector3(0.06f, 0f, 0f), new Vector3(0.12f, 0.04f, 0.09f));

            Visual(PrimitiveType.Capsule, "LeftThighVisual", leftUpperLeg, new Vector3(0f, -0.215f, 0f), new Vector3(0.14f, 0.23f, 0.14f));
            Visual(PrimitiveType.Capsule, "LeftShinVisual", leftLowerLeg, new Vector3(0f, -0.205f, 0f), new Vector3(0.115f, 0.215f, 0.115f));
            Visual(PrimitiveType.Cube, "LeftFootVisual", leftFoot, new Vector3(0f, -0.045f, 0.05f), new Vector3(0.10f, 0.07f, 0.24f));

            Visual(PrimitiveType.Capsule, "RightThighVisual", rightUpperLeg, new Vector3(0f, -0.215f, 0f), new Vector3(0.14f, 0.23f, 0.14f));
            Visual(PrimitiveType.Capsule, "RightShinVisual", rightLowerLeg, new Vector3(0f, -0.205f, 0f), new Vector3(0.115f, 0.215f, 0.115f));
            Visual(PrimitiveType.Cube, "RightFootVisual", rightFoot, new Vector3(0f, -0.045f, 0.05f), new Vector3(0.10f, 0.07f, 0.24f));
        }

        private static Transform Bone(string boneName, Transform parent, Vector3 worldPosition)
        {
            Transform bone = new GameObject(boneName).transform;
            bone.SetParent(parent, false);
            bone.position = worldPosition;
            return bone;
        }

        private static Transform Visual(PrimitiveType type, string visualName, Transform bone, Vector3 localPosition, Vector3 localScale, Vector3 localEuler = default)
        {
            GameObject visual = GameObject.CreatePrimitive(type);
            visual.name = visualName;

            // The CharacterController is the player's only collider; primitive
            // colliders would block it and spam ground checks.
            Object.DestroyImmediate(visual.GetComponent<Collider>());

            Transform t = visual.transform;
            t.SetParent(bone, false);
            t.localPosition = localPosition;
            t.localRotation = Quaternion.Euler(localEuler);
            t.localScale = localScale;
            return t;
        }

        // ------------------------------------------------------------------
        // Humanoid Avatar baking
        // ------------------------------------------------------------------

        private static Avatar BakeHumanoidAvatar(GameObject root)
        {
            var humanBones = new List<HumanBone>();
            MapBone(humanBones, HumanBodyBones.Hips, "Hips");
            MapBone(humanBones, HumanBodyBones.Spine, "Spine");
            MapBone(humanBones, HumanBodyBones.Chest, "Chest");
            MapBone(humanBones, HumanBodyBones.Neck, "Neck");
            MapBone(humanBones, HumanBodyBones.Head, "Head");
            MapBone(humanBones, HumanBodyBones.LeftUpperArm, "LeftUpperArm");
            MapBone(humanBones, HumanBodyBones.LeftLowerArm, "LeftLowerArm");
            MapBone(humanBones, HumanBodyBones.LeftHand, "LeftHand");
            MapBone(humanBones, HumanBodyBones.RightUpperArm, "RightUpperArm");
            MapBone(humanBones, HumanBodyBones.RightLowerArm, "RightLowerArm");
            MapBone(humanBones, HumanBodyBones.RightHand, "RightHand");
            MapBone(humanBones, HumanBodyBones.LeftUpperLeg, "LeftUpperLeg");
            MapBone(humanBones, HumanBodyBones.LeftLowerLeg, "LeftLowerLeg");
            MapBone(humanBones, HumanBodyBones.LeftFoot, "LeftFoot");
            MapBone(humanBones, HumanBodyBones.RightUpperLeg, "RightUpperLeg");
            MapBone(humanBones, HumanBodyBones.RightLowerLeg, "RightLowerLeg");
            MapBone(humanBones, HumanBodyBones.RightFoot, "RightFoot");

            // The skeleton must describe every transform under (and including)
            // the root in its current (T-pose) configuration.
            Transform[] allTransforms = root.GetComponentsInChildren<Transform>();
            var skeleton = new SkeletonBone[allTransforms.Length];
            for (int i = 0; i < allTransforms.Length; i++)
            {
                skeleton[i] = new SkeletonBone
                {
                    name = allTransforms[i].name,
                    position = allTransforms[i].localPosition,
                    rotation = allTransforms[i].localRotation,
                    scale = allTransforms[i].localScale
                };
            }

            var description = new HumanDescription
            {
                human = humanBones.ToArray(),
                skeleton = skeleton,
                upperArmTwist = 0.5f,
                lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                lowerLegTwist = 0.5f,
                armStretch = 0.05f,
                legStretch = 0.05f,
                feetSpacing = 0f,
                hasTranslationDoF = false
            };

            Avatar avatar = AvatarBuilder.BuildHumanAvatar(root, description);
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                Debug.LogError("[PlayerRigBuilder] Humanoid Avatar baking failed; the Animator will have no avatar. Check the console for muscle/T-pose errors.");
                return null;
            }

            avatar.name = "PlayerCapsuleAvatar";
            EnsureFolder(AvatarFolder);
            AssetDatabase.DeleteAsset(AvatarAssetPath);
            AssetDatabase.CreateAsset(avatar, AvatarAssetPath);
            AssetDatabase.SaveAssets();
            return avatar;
        }

        private static void MapBone(List<HumanBone> humanBones, HumanBodyBones humanBone, string sceneBoneName)
        {
            humanBones.Add(new HumanBone
            {
                // HumanTrait.BoneName holds Mecanim's canonical human bone names.
                humanName = HumanTrait.BoneName[(int)humanBone],
                boneName = sceneBoneName,
                limit = new HumanLimit { useDefaultValues = true }
            });
        }

        // ------------------------------------------------------------------
        // Camera rig, CharacterController, components
        // ------------------------------------------------------------------

        private static void BuildCameraRig(Transform root)
        {
            Transform head = root.Find("Hips/Spine/Chest/Neck/Head");

            // Pivot carries pitch only; yaw lives on the root. Positioned at eye
            // height, slightly forward of the head bone.
            Transform pivot = new GameObject("CameraPivot").transform;
            pivot.SetParent(head, false);
            pivot.localPosition = new Vector3(0f, 0.09f, 0.12f);

            GameObject cameraObject = new GameObject("PlayerCamera");
            cameraObject.transform.SetParent(pivot, false);
            cameraObject.tag = "MainCamera";

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.fieldOfView = 65f;
            cameraObject.AddComponent<AudioListener>();
        }

        private static void AddCharacterController(GameObject root)
        {
            CharacterController controller = root.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.radius = 0.35f;
            controller.slopeLimit = 45f;
            controller.stepOffset = 0.4f;
            controller.skinWidth = 0.03f;
            controller.minMoveDistance = 0f;
        }

        private static void AddAndWireComponents(GameObject root)
        {
            PlayerInputHandler inputHandler = root.AddComponent<PlayerInputHandler>();
            root.AddComponent<PlayerStateMachine>();
            root.AddComponent<PlayerLocomotion>();
            root.AddComponent<PlayerCrouch>();
            root.AddComponent<PlayerProne>();
            root.AddComponent<PlayerAnimationController>();
            root.AddComponent<PlayerFootGrounder>();
            root.AddComponent<PlayerCameraEffects>();
            root.AddComponent<PlayerFootsteps>();
            root.AddComponent<FirstPersonCameraController>();

            // If the Phase 6 controller has been generated, wire it up too.
            var animatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/_Game/Player/Animations/PlayerAnimatorController.controller");
            if (animatorController != null)
                root.GetComponent<Animator>().runtimeAnimatorController = animatorController;
            PlayerReferences references = root.AddComponent<PlayerReferences>();

            var inputAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputAssetPath);
            if (inputAsset == null)
            {
                Debug.LogError($"[PlayerRigBuilder] Input asset not found at '{InputAssetPath}'. Assign it manually on PlayerInputHandler.");
            }
            else
            {
                var serialized = new SerializedObject(inputHandler);
                serialized.FindProperty("actions").objectReferenceValue = inputAsset;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            references.ResolveReferences();
            EditorUtility.SetDirty(references);
        }

        private static void WarnAboutOtherCameras(GameObject root)
        {
            foreach (Camera sceneCamera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (!sceneCamera.transform.IsChildOf(root.transform))
                    Debug.LogWarning($"[PlayerRigBuilder] Another camera '{sceneCamera.name}' exists in the scene. Disable or delete it so the player camera renders.", sceneCamera);
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            int lastSlash = path.LastIndexOf('/');
            string parent = path.Substring(0, lastSlash);
            string leaf = path.Substring(lastSlash + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
