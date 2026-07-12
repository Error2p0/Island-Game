using IslandGame.Creatures;
using IslandGame.Data.Creatures;
using IslandGame.Data.Items;
using IslandGame.Data.Stats;
using IslandGame.Stats;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Creates the example creature content: loot items (Raw Meat, Hide),
    /// two primitive-built quadruped prefabs (Deer — Passive, Boar — Hostile)
    /// using the player rig's technique (bone transforms + collider-less
    /// primitive visuals under them), procedurally generated Idle/Walk clips
    /// with a Speed01 Simple1D blend (the player animation builder's approach
    /// on a generic rig — transform curves instead of humanoid muscles), and
    /// the CreatureDefinition assets wiring stats + loot + prefab together.
    ///
    /// Idempotent like every content creator: existing assets are left
    /// untouched, only missing pieces are created.
    /// </summary>
    public static class CreatureContentCreator
    {
        private const string CreatureFolder = "Assets/_Game/Content/Creatures";
        private const string PrefabFolder = CreatureFolder + "/Prefabs";
        private const string AnimationFolder = CreatureFolder + "/Animations";
        private const string MaterialFolder = CreatureFolder + "/Materials";
        private const string ItemFolder = "Assets/_Game/Content/Items";
        private const string IconFolder = "Assets/_Game/Content/Textures/Icons";

        [MenuItem("Island Game/Data/Create Example Creatures")]
        public static void Create()
        {
            DefinitionDatabaseSync.EnsureFolderExists(CreatureFolder);
            DefinitionDatabaseSync.EnsureFolderExists(PrefabFolder);
            DefinitionDatabaseSync.EnsureFolderExists(AnimationFolder);
            DefinitionDatabaseSync.EnsureFolderExists(MaterialFolder);
            DefinitionDatabaseSync.EnsureFolderExists(ItemFolder);
            DefinitionDatabaseSync.EnsureFolderExists(IconFolder);

            // Stats first — creature definitions reference them.
            StatContentCreator.Create();

            // --- Loot items ------------------------------------------------
            ItemDefinition rawMeat = CreateLootItem(
                "raw_meat", "Raw Meat", "Fresh game. Edible raw in a pinch; the campfire makes it worth eating.",
                RawMeatPixel, maxStack: 10, weightKg: 0.5f);
            MakeEdible(rawMeat, hungerRestore: 12f, thirstRestore: 2f);

            ItemDefinition hide = CreateLootItem(
                "hide", "Hide", "A rough animal pelt. Warm clothing and bedding, eventually.",
                HidePixel, maxStack: 20, weightKg: 0.8f);

            // Combat-phase loot: crafting materials for future recipes.
            ItemDefinition bone = CreateLootItem(
                "bone", "Bone", "Sturdy animal bone. Tools, arrows, grim decor.",
                BonePixel, maxStack: 30, weightKg: 0.3f);

            ItemDefinition claw = CreateLootItem(
                "claw", "Claw", "A night stalker's talon. Sharp enough to be a crafting component on its own.",
                ClawPixel, maxStack: 30, weightKg: 0.1f);

            // --- Materials ---------------------------------------------------
            Material deerMaterial = GetOrCreateMaterial("CreatureDeer", new Color(0.55f, 0.40f, 0.26f));
            Material boarMaterial = GetOrCreateMaterial("CreatureBoar", new Color(0.30f, 0.26f, 0.23f));
            Material tuskMaterial = GetOrCreateMaterial("CreatureTusk", new Color(0.88f, 0.85f, 0.74f));
            Material goatMaterial = GetOrCreateMaterial("CreatureGoat", new Color(0.76f, 0.70f, 0.58f));
            Material wolfMaterial = GetOrCreateMaterial("CreatureWolf", new Color(0.44f, 0.45f, 0.48f));
            Material stalkerMaterial = GetOrCreateMaterial("CreatureStalker", new Color(0.16f, 0.13f, 0.21f));

            // --- Prefabs -----------------------------------------------------
            GameObject deerPrefab = BuildQuadrupedPrefab(
                "Deer", bodyHeight: 0.62f, bodyScale: new Vector3(0.42f, 0.48f, 0.42f),
                headLocalPosition: new Vector3(0f, 0.24f, 0.52f), headScale: new Vector3(0.2f, 0.24f, 0.3f),
                legScale: new Vector3(0.09f, 0.3f, 0.09f), colliderHeight: 1.15f, colliderRadius: 0.35f,
                bodyMaterial: deerMaterial);

            GameObject boarPrefab = BuildQuadrupedPrefab(
                "Boar", bodyHeight: 0.48f, bodyScale: new Vector3(0.55f, 0.5f, 0.5f),
                headLocalPosition: new Vector3(0f, 0.05f, 0.5f), headScale: new Vector3(0.3f, 0.28f, 0.34f),
                legScale: new Vector3(0.11f, 0.23f, 0.11f), colliderHeight: 0.95f, colliderRadius: 0.4f,
                bodyMaterial: boarMaterial, ornament: HeadOrnament.Tusks, ornamentMaterial: tuskMaterial);

            GameObject goatPrefab = BuildQuadrupedPrefab(
                "Goat", bodyHeight: 0.55f, bodyScale: new Vector3(0.4f, 0.42f, 0.4f),
                headLocalPosition: new Vector3(0f, 0.2f, 0.46f), headScale: new Vector3(0.19f, 0.22f, 0.28f),
                legScale: new Vector3(0.085f, 0.26f, 0.085f), colliderHeight: 1.0f, colliderRadius: 0.32f,
                bodyMaterial: goatMaterial, ornament: HeadOrnament.Horns, ornamentMaterial: tuskMaterial);

            GameObject wolfPrefab = BuildQuadrupedPrefab(
                "Wolf", bodyHeight: 0.52f, bodyScale: new Vector3(0.38f, 0.55f, 0.4f),
                headLocalPosition: new Vector3(0f, 0.12f, 0.55f), headScale: new Vector3(0.22f, 0.22f, 0.34f),
                legScale: new Vector3(0.09f, 0.26f, 0.09f), colliderHeight: 1.0f, colliderRadius: 0.35f,
                bodyMaterial: wolfMaterial);

            GameObject stalkerPrefab = BuildQuadrupedPrefab(
                "Stalker", bodyHeight: 0.66f, bodyScale: new Vector3(0.44f, 0.58f, 0.46f),
                headLocalPosition: new Vector3(0f, 0.2f, 0.58f), headScale: new Vector3(0.26f, 0.24f, 0.34f),
                legScale: new Vector3(0.09f, 0.34f, 0.09f), colliderHeight: 1.25f, colliderRadius: 0.4f,
                bodyMaterial: stalkerMaterial, ornament: HeadOrnament.Horns, ornamentMaterial: stalkerMaterial);

            // Combat-phase migration: BuildQuadrupedPrefab early-outs for
            // existing prefabs, so pre-combat controllers would keep missing
            // the Attack state — ensure it explicitly for every species
            // (no-op on controllers that already have it).
            BuildQuadrupedAnimator("Deer", 0.62f, new Vector3(0f, 0.24f, 0.52f));
            BuildQuadrupedAnimator("Boar", 0.48f, new Vector3(0f, 0.05f, 0.5f));
            BuildQuadrupedAnimator("Goat", 0.55f, new Vector3(0f, 0.2f, 0.46f));
            BuildQuadrupedAnimator("Wolf", 0.52f, new Vector3(0f, 0.12f, 0.55f));
            BuildQuadrupedAnimator("Stalker", 0.66f, new Vector3(0f, 0.2f, 0.58f));

            // --- Definitions -------------------------------------------------
            CreateCreatureDefinition(
                "deer", "Deer", "Skittish island game. Freezes when it spots you; bolts when crowded or hurt.",
                CreatureAggression.Passive, deerPrefab,
                statValues: new[]
                {
                    (StatIds.Health, 30f), (StatIds.MoveSpeed, 5.5f), (StatIds.DetectionRadius, 14f),
                },
                loot: new[]
                {
                    (rawMeat, 1, 2, 1f), (hide, 1, 1, 0.5f),
                },
                fleeTriggerDistance: 7f);

            CreateCreatureDefinition(
                "boar", "Boar", "Territorial and mean. It sees you, it charges.",
                CreatureAggression.Hostile, boarPrefab,
                statValues: new[]
                {
                    (StatIds.Health, 60f), (StatIds.MoveSpeed, 4.2f),
                    (StatIds.DetectionRadius, 12f), (StatIds.AttackDamage, 8f),
                },
                loot: new[]
                {
                    (rawMeat, 2, 3, 1f), (hide, 1, 2, 0.75f),
                },
                fleeTriggerDistance: 6f,
                attackDamageType: DamageType.Pierce, packAlertRadius: 8f);

            CreateCreatureDefinition(
                "goat", "Goat", "A stubborn island grazer. Good eating, if you can catch one.",
                CreatureAggression.Passive, goatPrefab,
                statValues: new[]
                {
                    (StatIds.Health, 25f), (StatIds.MoveSpeed, 5.0f), (StatIds.DetectionRadius, 12f),
                },
                loot: new[]
                {
                    (rawMeat, 1, 2, 1f), (hide, 1, 1, 0.6f), (bone, 1, 2, 0.5f),
                },
                fleeTriggerDistance: 6f,
                packAlertRadius: 10f); // one spooked goat scatters the herd

            CreateCreatureDefinition(
                "wolf", "Wolf", "Leaves you alone — until you don't. Attack one and the pack answers.",
                CreatureAggression.Neutral, wolfPrefab,
                statValues: new[]
                {
                    (StatIds.Health, 40f), (StatIds.MoveSpeed, 6.5f),
                    (StatIds.DetectionRadius, 16f), (StatIds.AttackDamage, 10f),
                },
                loot: new[]
                {
                    (rawMeat, 1, 2, 1f), (hide, 1, 1, 0.6f), (bone, 1, 2, 0.8f),
                },
                fleeTriggerDistance: 6f,
                attackDamageType: DamageType.Slash, packAlertRadius: 16f);

            CreateCreatureDefinition(
                "stalker", "Night Stalker", "Something the dark grows. Fast, sharp-sensed and only ever seen after sundown.",
                CreatureAggression.Hostile, stalkerPrefab,
                statValues: new[]
                {
                    (StatIds.Health, 45f), (StatIds.MoveSpeed, 5.8f),
                    (StatIds.DetectionRadius, 18f), (StatIds.AttackDamage, 12f),
                },
                loot: new[]
                {
                    (claw, 1, 2, 1f), (bone, 1, 3, 0.8f), (hide, 1, 1, 0.3f),
                },
                fleeTriggerDistance: 6f,
                attackDamageType: DamageType.Slash, packAlertRadius: 14f);

            AssetDatabase.SaveAssets();
            DefinitionDatabaseSync.SyncAll();

            Debug.Log(
                "Example creatures ready: items Raw Meat/Hide/Bone/Claw; prefabs Deer, Boar, Goat, Wolf, Stalker " +
                "with generated Idle/Walk/Attack animation; definitions with stats + loot tables; databases synced. " +
                "Existing assets were left untouched (existing animator controllers got the Attack state retrofitted).");
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(CreatureFolder));
        }

        // ------------------------------------------------------------------
        // Example spawners (scene setup)
        // ------------------------------------------------------------------

        [MenuItem("Island Game/World/Create Example Creature Spawners")]
        public static void CreateExampleSpawners()
        {
            GameObject parent = GameObject.Find("CreatureSpawners");
            if (parent == null)
            {
                parent = new GameObject("CreatureSpawners");
                Undo.RegisterCreatedObjectUndo(parent, "Create Creature Spawners");
            }

            // Heights are nominal — spawn placement scans the voxel column for
            // real ground at runtime, so only the XZ location matters much.
            CreateSpawner(parent.transform, "DeerSpawner", "deer", new Vector3(18f, 52f, 14f), maxPopulation: 3);
            CreateSpawner(parent.transform, "BoarSpawner", "boar", new Vector3(-24f, 52f, -18f), maxPopulation: 2,
                nightPopulationBonus: 1, nightSpawnIntervalMultiplier: 0.6f, nightDetectionRadiusBonus: 0.5f);
            CreateSpawner(parent.transform, "GoatSpawner", "goat", new Vector3(30f, 52f, -26f), maxPopulation: 4);
            CreateSpawner(parent.transform, "WolfSpawner", "wolf", new Vector3(-38f, 52f, 24f), maxPopulation: 3);
            CreateSpawner(parent.transform, "StalkerSpawner", "stalker", new Vector3(8f, 52f, -40f), maxPopulation: 3,
                spawnOnlyAtNight: true, nightSpawnIntervalMultiplier: 0.5f, nightDetectionRadiusBonus: 0.5f);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(parent.scene);
            Debug.Log(
                "Creature spawners ready under 'CreatureSpawners': Deer (3), Boar (2, night-boosted), Goat (4), " +
                "Wolf (3, neutral pack), Stalker (3, NIGHT ONLY). Save the scene.");
        }

        private static void CreateSpawner(
            Transform parent, string objectName, string creatureId, Vector3 position, int maxPopulation,
            bool spawnOnlyAtNight = false, int nightPopulationBonus = 0,
            float nightSpawnIntervalMultiplier = 1f, float nightDetectionRadiusBonus = 0f)
        {
            if (parent.Find(objectName) != null)
                return; // idempotent

            var definition = AssetDatabase.LoadAssetAtPath<CreatureDefinition>(
                $"{CreatureFolder}/{ToAssetName(creatureId)}.asset");
            if (definition == null)
            {
                Debug.LogError($"Spawner '{objectName}': creature '{creatureId}' not found — run Island Game/Data/Create Example Creatures first.");
                return;
            }

            var spawnerObject = new GameObject(objectName);
            Undo.RegisterCreatedObjectUndo(spawnerObject, "Create Creature Spawners");
            spawnerObject.transform.SetParent(parent, false);
            spawnerObject.transform.position = position;

            var spawner = spawnerObject.AddComponent<CreatureSpawner>();
            var serialized = new SerializedObject(spawner);
            serialized.FindProperty("definition").objectReferenceValue = definition;
            serialized.FindProperty("maxPopulation").intValue = maxPopulation;
            serialized.FindProperty("spawnOnlyAtNight").boolValue = spawnOnlyAtNight;
            serialized.FindProperty("nightPopulationBonus").intValue = nightPopulationBonus;
            serialized.FindProperty("nightSpawnIntervalMultiplier").floatValue = nightSpawnIntervalMultiplier;
            serialized.FindProperty("nightDetectionRadiusBonus").floatValue = nightDetectionRadiusBonus;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------
        // Prefab building (player rig technique: bones + collider-less visuals)
        // ------------------------------------------------------------------

        /// <summary>Head decoration variants shared by the quadruped builder.</summary>
        private enum HeadOrnament
        {
            None,
            Tusks,
            Horns,
        }

        private static GameObject BuildQuadrupedPrefab(
            string creatureName, float bodyHeight, Vector3 bodyScale,
            Vector3 headLocalPosition, Vector3 headScale, Vector3 legScale,
            float colliderHeight, float colliderRadius,
            Material bodyMaterial, HeadOrnament ornament = HeadOrnament.None, Material ornamentMaterial = null)
        {
            string prefabPath = $"{PrefabFolder}/{creatureName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null)
                return existing;

            var root = new GameObject(creatureName);
            try
            {
                // --- Bones (limb separation for animation) -----------------
                Transform body = Bone("Body", root.transform, new Vector3(0f, bodyHeight, 0f));
                Transform head = Bone("Head", body, headLocalPosition);
                float hipY = -bodyScale.y * 0.35f;
                float hipX = bodyScale.x * 0.42f;
                float hipZ = bodyScale.z * 0.75f;
                Transform legFL = Bone("LegFL", body, new Vector3(-hipX, hipY, hipZ));
                Transform legFR = Bone("LegFR", body, new Vector3(hipX, hipY, hipZ));
                Transform legBL = Bone("LegBL", body, new Vector3(-hipX, hipY, -hipZ));
                Transform legBR = Bone("LegBR", body, new Vector3(hipX, hipY, -hipZ));

                // --- Visuals ------------------------------------------------
                // Horizontal capsule body: capsule axis is Y, so pitch it 90°.
                Visual(PrimitiveType.Capsule, "BodyVisual", body, Vector3.zero,
                    new Vector3(bodyScale.x, bodyScale.z, bodyScale.y), new Vector3(90f, 0f, 0f), bodyMaterial);
                Visual(PrimitiveType.Cube, "HeadVisual", head, Vector3.zero, headScale, Vector3.zero, bodyMaterial);
                Visual(PrimitiveType.Cube, "EarL", head, new Vector3(-headScale.x * 0.4f, headScale.y * 0.7f, -headScale.z * 0.2f),
                    new Vector3(0.05f, 0.1f, 0.03f), Vector3.zero, bodyMaterial);
                Visual(PrimitiveType.Cube, "EarR", head, new Vector3(headScale.x * 0.4f, headScale.y * 0.7f, -headScale.z * 0.2f),
                    new Vector3(0.05f, 0.1f, 0.03f), Vector3.zero, bodyMaterial);

                if (ornament == HeadOrnament.Tusks && ornamentMaterial != null)
                {
                    Visual(PrimitiveType.Cube, "TuskL", head, new Vector3(-headScale.x * 0.35f, -headScale.y * 0.35f, headScale.z * 0.5f),
                        new Vector3(0.04f, 0.04f, 0.14f), new Vector3(-20f, 0f, 0f), ornamentMaterial);
                    Visual(PrimitiveType.Cube, "TuskR", head, new Vector3(headScale.x * 0.35f, -headScale.y * 0.35f, headScale.z * 0.5f),
                        new Vector3(0.04f, 0.04f, 0.14f), new Vector3(-20f, 0f, 0f), ornamentMaterial);
                }
                else if (ornament == HeadOrnament.Horns && ornamentMaterial != null)
                {
                    Visual(PrimitiveType.Cube, "HornL", head, new Vector3(-headScale.x * 0.35f, headScale.y * 0.55f, -headScale.z * 0.1f),
                        new Vector3(0.045f, 0.16f, 0.045f), new Vector3(-30f, 0f, -12f), ornamentMaterial);
                    Visual(PrimitiveType.Cube, "HornR", head, new Vector3(headScale.x * 0.35f, headScale.y * 0.55f, -headScale.z * 0.1f),
                        new Vector3(0.045f, 0.16f, 0.045f), new Vector3(-30f, 0f, 12f), ornamentMaterial);
                }

                // Legs hang from hip pivots so localEulerAngles.x swings them.
                float legDrop = bodyHeight + hipY;
                foreach (Transform leg in new[] { legFL, legFR, legBL, legBR })
                {
                    Visual(PrimitiveType.Capsule, leg.name + "Visual", leg,
                        new Vector3(0f, -legDrop * 0.5f, 0f),
                        new Vector3(legScale.x, legDrop * 0.55f, legScale.z), Vector3.zero, bodyMaterial);
                }

                // --- Animation ----------------------------------------------
                AnimatorController controller = BuildQuadrupedAnimator(creatureName, bodyHeight, headLocalPosition);
                Animator animator = root.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                // --- Physics & gameplay components -------------------------
                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.center = new Vector3(0f, colliderHeight * 0.5f, 0f);
                capsule.height = colliderHeight;
                capsule.radius = colliderRadius;

                var body3D = root.AddComponent<Rigidbody>();
                body3D.isKinematic = true;
                body3D.useGravity = false;

                root.AddComponent<StatContainer>();
                root.AddComponent<Creature>();
                root.AddComponent<CreatureMover>();
                root.AddComponent<CreatureAI>();

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log($"Built creature prefab '{creatureName}' at {prefabPath}.", prefab);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Transform Bone(string boneName, Transform parent, Vector3 localPosition)
        {
            Transform bone = new GameObject(boneName).transform;
            bone.SetParent(parent, false);
            bone.localPosition = localPosition;
            return bone;
        }

        private static void Visual(
            PrimitiveType type, string visualName, Transform bone,
            Vector3 localPosition, Vector3 localScale, Vector3 localEuler, Material material)
        {
            GameObject visual = GameObject.CreatePrimitive(type);
            visual.name = visualName;

            // The root capsule is the creature's only collider — same rule as
            // the player rig (primitive colliders would trap weapon casts).
            Object.DestroyImmediate(visual.GetComponent<Collider>());

            Transform t = visual.transform;
            t.SetParent(bone, false);
            t.localPosition = localPosition;
            t.localRotation = Quaternion.Euler(localEuler);
            t.localScale = localScale;

            if (material != null)
                visual.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        // ------------------------------------------------------------------
        // Animation (generic-rig transform curves; Speed01 Simple1D blend)
        // ------------------------------------------------------------------

        private static AnimatorController BuildQuadrupedAnimator(
            string creatureName, float bodyHeight, Vector3 headLocalPosition)
        {
            string controllerPath = $"{AnimationFolder}/{creatureName}Animator.controller";
            var existingController = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (existingController != null)
            {
                // Combat-phase migration: creature-phase controllers predate
                // the Attack state — retrofit it without touching the rest.
                EnsureAttackState(existingController, creatureName, bodyHeight);
                return existingController;
            }

            AnimationClip idle = BuildIdleClip(creatureName, bodyHeight, headLocalPosition);
            AnimationClip walk = BuildWalkClip(creatureName, bodyHeight);

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("Speed01", AnimatorControllerParameterType.Float);

            AnimatorState state = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed01";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, 1f);
            controller.layers[0].stateMachine.defaultState = state;

            EnsureAttackState(controller, creatureName, bodyHeight);
            return controller;
        }

        /// <summary>
        /// Adds the Attack trigger + lunge state when missing (idempotent —
        /// safe on both fresh and creature-phase controllers): AnyState →
        /// Attack on the trigger, back to the default state on exit time.
        /// CreatureAI's timed hit window is data (AttackWindupSeconds), so the
        /// clip only needs to LOOK like a lunge, not encode gameplay timing.
        /// </summary>
        private static void EnsureAttackState(AnimatorController controller, string creatureName, float bodyHeight)
        {
            foreach (AnimatorControllerParameter parameter in controller.parameters)
            {
                if (parameter.name == "Attack")
                    return; // already migrated
            }

            string clipPath = $"{AnimationFolder}/{creatureName}_Attack.anim";
            var attackClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (attackClip == null)
                attackClip = BuildAttackClip(creatureName, bodyHeight);

            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            AnimatorState attackState = stateMachine.AddState("Attack");
            attackState.motion = attackClip;

            AnimatorStateTransition enter = stateMachine.AddAnyStateTransition(attackState);
            enter.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
            enter.duration = 0.05f;
            enter.canTransitionToSelf = false;

            AnimatorStateTransition exit = attackState.AddTransition(stateMachine.defaultState);
            exit.hasExitTime = true;
            exit.exitTime = 0.95f;
            exit.duration = 0.1f;

            EditorUtility.SetDirty(controller);
        }

        private static AnimationClip BuildAttackClip(string creatureName, float bodyHeight)
        {
            const float duration = 0.55f;
            var clip = new AnimationClip { name = $"{creatureName}_Attack", frameRate = 60f };
            // Non-looping: one lunge per trigger (NewClip() defaults to loop).

            // Rear back, lunge forward-and-down, recover — same authored-curve
            // technique as every generated clip, on the generic rig.
            var pitch = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.15f * duration, -10f),
                new Keyframe(0.45f * duration, 16f), new Keyframe(duration, 0f));
            var thrust = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.15f * duration, -0.1f),
                new Keyframe(0.45f * duration, 0.28f), new Keyframe(duration, 0f));
            for (int i = 0; i < pitch.length; i++)
            {
                pitch.SmoothTangents(i, 0f);
                thrust.SmoothTangents(i, 0f);
            }

            clip.SetCurve("Body", typeof(Transform), "localEulerAngles.x", pitch);
            clip.SetCurve("Body", typeof(Transform), "localPosition.z", thrust);
            clip.SetCurve("Body", typeof(Transform), "localPosition.y", Constant(duration, bodyHeight));

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            return SaveClip(clip);
        }

        private static AnimationClip BuildIdleClip(string creatureName, float bodyHeight, Vector3 headLocalPosition)
        {
            const float duration = 3f;
            AnimationClip clip = NewClip($"{creatureName}_Idle");

            // Gentle breathing bob on the body, tiny head sway. The body's
            // localPosition.y curve MUST orbit its rest height — an animated
            // property fully overrides the authored transform.
            clip.SetCurve("Body", typeof(Transform), "localPosition.y",
                Sine(duration, 1f, 0.015f, bodyHeight));
            clip.SetCurve("Body/Head", typeof(Transform), "localEulerAngles.x",
                Sine(duration, 0.66f, 3.5f, 0f));
            clip.SetCurve("Body/Head", typeof(Transform), "localPosition.y",
                Constant(duration, headLocalPosition.y));

            return SaveClip(clip);
        }

        private static AnimationClip BuildWalkClip(string creatureName, float bodyHeight)
        {
            const float duration = 0.7f; // one full stride cycle
            AnimationClip clip = NewClip($"{creatureName}_Walk");

            // Diagonal pairs swing in anti-phase (trot): FL+BR vs FR+BL.
            clip.SetCurve("Body/LegFL", typeof(Transform), "localEulerAngles.x", Sine(duration, 1f, 28f, 0f));
            clip.SetCurve("Body/LegBR", typeof(Transform), "localEulerAngles.x", Sine(duration, 1f, 28f, 0f));
            clip.SetCurve("Body/LegFR", typeof(Transform), "localEulerAngles.x", Sine(duration, 1f, -28f, 0f));
            clip.SetCurve("Body/LegBL", typeof(Transform), "localEulerAngles.x", Sine(duration, 1f, -28f, 0f));

            // Body bobs twice per stride.
            clip.SetCurve("Body", typeof(Transform), "localPosition.y",
                Sine(duration, 2f, 0.025f, bodyHeight));

            return SaveClip(clip);
        }

        private static AnimationClip NewClip(string clipName)
        {
            var clip = new AnimationClip { name = clipName, frameRate = 60f };
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        private static AnimationClip SaveClip(AnimationClip clip)
        {
            AssetDatabase.CreateAsset(clip, $"{AnimationFolder}/{clip.name}.anim");
            return clip;
        }

        /// <summary>Sampled sine so the loop's first and last keys match exactly (seamless cycles).</summary>
        private static AnimationCurve Sine(float duration, float cycles, float amplitude, float offset)
        {
            const int samples = 16;
            var curve = new AnimationCurve();
            for (int i = 0; i <= samples; i++)
            {
                float t = (float)i / samples;
                curve.AddKey(new Keyframe(t * duration, offset + Mathf.Sin(t * cycles * Mathf.PI * 2f) * amplitude));
            }

            for (int i = 0; i < curve.length; i++)
                curve.SmoothTangents(i, 0f);
            return curve;
        }

        private static AnimationCurve Constant(float duration, float value)
        {
            return new AnimationCurve(new Keyframe(0f, value), new Keyframe(duration, value));
        }

        // ------------------------------------------------------------------
        // Items, materials, definitions
        // ------------------------------------------------------------------

        private static ItemDefinition CreateLootItem(
            string id, string displayName, string description,
            ExampleContentCreator.PixelFunc iconPixel, int maxStack, float weightKg)
        {
            string path = $"{ItemFolder}/{ToAssetName(id)}.asset";
            ItemDefinition item = ExampleContentCreator.CreateOrLoad<ItemDefinition>(path, out bool created);
            if (!created)
                return item;

            Sprite icon = ExampleContentCreator.LoadSprite(
                ExampleContentCreator.CreateTexture($"{IconFolder}/icon_{id}.png", 64, iconPixel, asSprite: true));
            ExampleContentCreator.SetItemFields(item, id, displayName, description,
                icon, ItemCategory.Resource, maxStack, weightKg);
            return item;
        }

        private static void MakeEdible(ItemDefinition item, float hungerRestore, float thirstRestore)
        {
            if (item == null)
                return;

            var serialized = new SerializedObject(item);
            if ((ItemCategory)serialized.FindProperty("category").intValue == ItemCategory.Consumable)
                return; // pre-existing, already configured

            serialized.FindProperty("category").intValue = (int)ItemCategory.Consumable;
            serialized.FindProperty("hungerRestore").floatValue = hungerRestore;
            serialized.FindProperty("thirstRestore").floatValue = thirstRestore;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Material GetOrCreateMaterial(string materialName, Color color)
        {
            string path = $"{MaterialFolder}/{materialName}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;

            Shader lit = GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline.defaultShader
                : Shader.Find("Standard");

            var material = new Material(lit) { name = materialName, color = color };
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0f);

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void CreateCreatureDefinition(
            string id, string displayName, string description,
            CreatureAggression aggression, GameObject prefab,
            (string statId, float baseValue)[] statValues,
            (ItemDefinition item, int countMin, int countMax, float dropChance)[] loot,
            float fleeTriggerDistance,
            DamageType attackDamageType = DamageType.Blunt, float packAlertRadius = 0f)
        {
            string path = $"{CreatureFolder}/{ToAssetName(id)}.asset";
            CreatureDefinition definition = ExampleContentCreator.CreateOrLoad<CreatureDefinition>(path, out bool created);
            if (!created)
                return; // never overwrite hand-tuned creatures

            var serialized = new SerializedObject(definition);
            serialized.FindProperty("id").stringValue = id;
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("description").stringValue = description;
            serialized.FindProperty("aggression").intValue = (int)aggression;
            serialized.FindProperty("prefab").objectReferenceValue = prefab;
            serialized.FindProperty("fleeTriggerDistance").floatValue = fleeTriggerDistance;
            serialized.FindProperty("attackDamageType").intValue = (int)attackDamageType;
            serialized.FindProperty("packAlertRadius").floatValue = packAlertRadius;

            // Stats resolve through the database BY ID — asset file names are
            // display-style ("Mining Speed.asset") and not derivable from ids.
            var statDatabase = AssetDatabase.LoadAssetAtPath<StatDatabase>(
                $"{DefinitionDatabaseSync.DatabaseFolder}/StatDatabase.asset");

            SerializedProperty statList = serialized.FindProperty("stats");
            statList.arraySize = statValues.Length;
            for (int i = 0; i < statValues.Length; i++)
            {
                StatDefinition stat = null;
                if (statDatabase == null || !statDatabase.TryGet(statValues[i].statId, out stat))
                    Debug.LogError($"Creature '{id}': stat '{statValues[i].statId}' not found in the StatDatabase — run Island Game/Data/Create Player Stat Definitions.");

                SerializedProperty element = statList.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("stat").objectReferenceValue = stat;
                element.FindPropertyRelative("baseValue").floatValue = statValues[i].baseValue;
            }

            SerializedProperty lootList = serialized.FindProperty("loot");
            lootList.arraySize = loot.Length;
            for (int i = 0; i < loot.Length; i++)
            {
                SerializedProperty element = lootList.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("item").objectReferenceValue = loot[i].item;
                element.FindPropertyRelative("countMin").intValue = loot[i].countMin;
                element.FindPropertyRelative("countMax").intValue = loot[i].countMax;
                element.FindPropertyRelative("dropChance").floatValue = loot[i].dropChance;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>lowercase_id → PascalCase asset name (raw_meat → RawMeat), matching the content-set convention.</summary>
        private static string ToAssetName(string id)
        {
            string[] parts = id.Split('_');
            var builder = new System.Text.StringBuilder();
            foreach (string part in parts)
            {
                if (part.Length == 0)
                    continue;
                builder.Append(char.ToUpperInvariant(part[0]));
                if (part.Length > 1)
                    builder.Append(part, 1, part.Length - 1);
            }

            return builder.ToString();
        }

        // ------------------------------------------------------------------
        // Icon pixel functions
        // ------------------------------------------------------------------

        private static Color32 RawMeatPixel(int x, int y, int size, System.Random random)
        {
            float u = (x + 0.5f) / size - 0.5f;
            float v = (y + 0.5f) / size - 0.5f;
            float r = Mathf.Sqrt(u * u + v * v);
            if (r > 0.42f)
                return new Color32(0, 0, 0, 0);

            // Red meat with pale marbling streaks.
            bool marble = ((x * 7 + y * 3) % 23) < 3;
            var meat = new Color(0.72f, 0.16f, 0.18f);
            var fat = new Color(0.9f, 0.75f, 0.7f);
            Color color = Color.Lerp(marble ? fat : meat, meat, r * 0.8f);
            return color;
        }

        private static Color32 BonePixel(int x, int y, int size, System.Random random)
        {
            // A diagonal shaft with knobbed ends.
            float u = (x + 0.5f) / size;
            float v = (y + 0.5f) / size;
            float alongAxis = (u + v) * 0.5f;                    // 0..1 along the diagonal
            float offAxis = Mathf.Abs(u - v) * 0.7071f;          // distance from the diagonal

            float shaftHalfWidth = 0.06f;
            bool onShaft = offAxis < shaftHalfWidth && alongAxis > 0.2f && alongAxis < 0.8f;
            bool onKnob = false;
            foreach (float knobCenter in new[] { 0.2f, 0.8f })
            {
                float du = alongAxis - knobCenter;
                if (du * du + offAxis * offAxis < 0.011f)
                    onKnob = true;
            }

            if (!onShaft && !onKnob)
                return new Color32(0, 0, 0, 0);

            var boneLight = new Color(0.93f, 0.90f, 0.82f);
            var boneShade = new Color(0.72f, 0.68f, 0.58f);
            return Color.Lerp(boneLight, boneShade, offAxis / 0.12f);
        }

        private static Color32 ClawPixel(int x, int y, int size, System.Random random)
        {
            // A curved talon: thick at the top-left root, tapering to the tip.
            float u = (x + 0.5f) / size;
            float v = (y + 0.5f) / size;
            float t = Mathf.Clamp01((u - 0.15f) / 0.7f);         // 0 at root, 1 at tip
            float curve = 0.72f - t * t * 0.45f;                  // arc downward
            float thickness = Mathf.Lerp(0.13f, 0.015f, t);

            if (u < 0.15f || u > 0.85f || Mathf.Abs(v - curve) > thickness)
                return new Color32(0, 0, 0, 0);

            var dark = new Color(0.2f, 0.16f, 0.24f);
            var edge = new Color(0.55f, 0.5f, 0.62f);
            return Color.Lerp(dark, edge, Mathf.Abs(v - curve) / Mathf.Max(0.01f, thickness));
        }

        private static Color32 HidePixel(int x, int y, int size, System.Random random)
        {
            float u = (x + 0.5f) / size;
            float v = (y + 0.5f) / size;
            if (u < 0.12f || u > 0.88f || v < 0.2f || v > 0.8f)
                return new Color32(0, 0, 0, 0);

            // Tan pelt, darker toward the ragged edge.
            float edge = Mathf.Min(Mathf.Min(u - 0.12f, 0.88f - u), Mathf.Min(v - 0.2f, 0.8f - v)) / 0.15f;
            var center = new Color(0.62f, 0.45f, 0.28f);
            var rim = new Color(0.4f, 0.28f, 0.16f);
            return Color.Lerp(rim, center, Mathf.Clamp01(edge));
        }
    }
}
