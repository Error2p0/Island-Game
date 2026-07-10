using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using IslandGame.Player;

namespace IslandGame.EditorTools
{
    /// <summary>
    /// One-click generator for the Phase 6 animation set: placeholder humanoid
    /// AnimationClips (authored in MUSCLE space so they retarget to any future
    /// imported character), the upper-body avatar mask, and the full Animator
    /// Controller (layers, blend trees, transitions), assigned to the scene
    /// Player. Everything under Assets/_Game/Player/Animations is GENERATED —
    /// don't hand-edit those assets; tweak the constants here and rebuild.
    /// </summary>
    public static class PlayerAnimationBuilder
    {
        private const string AnimationsFolder = "Assets/_Game/Player/Animations";
        private const string ControllerPath = AnimationsFolder + "/PlayerAnimatorController.controller";
        private const string UpperBodyMaskPath = AnimationsFolder + "/UpperBodyMask.mask";

        // ==== Pose tuning (eyeball-tuned placeholder values). Muscle sign
        // convention: the SECOND word of a muscle name is its positive direction
        // ("Down-Up": +1 = up). If a limb moves the wrong way, flip the sign
        // here and rebuild. ============================================
        private const float ArmsDown = -0.55f;        // arms relaxed at the sides (from T-pose)
        private const float ForearmRelax = 0.2f;      // slight elbow bend
        private const float WalkLegSwing = 0.35f;
        private const float WalkArmSwing = 0.25f;
        private const float SprintLegSwing = 0.6f;
        private const float SprintArmSwing = 0.5f;
        private const float SprintLean = -0.12f;      // spine forward lean while sprinting
        private const float CrouchThighBend = -0.6f;  // thighs forward
        private const float CrouchKneeBend = -0.8f;   // knees folded
        private const float PronePitch = 75f;         // body pitch toward face-down, degrees
        private const float SwimIdlePitch = 30f;      // treading water, mostly upright
        private const float SwimStrokePitch = 78f;    // swimming, nearly flat

        // Desired HIPS heights in METERS (real world units, not RootT units).
        // The builder calibrates the RootT.y <-> hips-height mapping against the
        // scene player's actual avatar at build time, so these are the numbers
        // you tune by eye.
        // Rig hips are AUTHORED at 0.95, but standing there leaves the feet
        // hovering: the soles are authored 0.03 above the root plane and the
        // CharacterController rides its 0.03 skinWidth above the ground, so
        // fully stretched legs bottom out ~6 cm short of the terrain. 0.89
        // drops the body exactly that much — soles kiss the ground with
        // straight legs, and PlayerFootGrounder's zero-correction case is
        // actually zero on flat ground.
        private const float StandHipsHeight = 0.89f;
        private const float CrouchHipsHeight = 0.55f;
        private const float ProneHipsHeight = 0.24f;
        private const float SwimIdleHipsHeight = 0.75f;
        private const float SwimStrokeHipsHeight = 0.9f;

        [MenuItem("Tools/Island Game/Build Player Animations && Controller")]
        public static void Build()
        {
            EnsureFolder("Assets/_Game/Player");
            if (AssetDatabase.IsValidFolder(AnimationsFolder))
                AssetDatabase.DeleteAsset(AnimationsFolder);
            AssetDatabase.CreateFolder("Assets/_Game/Player", "Animations");

            ResolveMuscleNames();
            BeginSceneSampling();

            AnimatorController controller;
            try
            {
                AnimationClip idle = BuildIdle();
                AnimationClip walk = BuildWalk();
                AnimationClip sprint = BuildSprint();
                AnimationClip crouchIdle = BuildCrouchIdle();
                AnimationClip crouchWalk = BuildCrouchWalk();
                AnimationClip proneIdle = BuildProneIdle();
                AnimationClip proneCrawl = BuildProneCrawl();
                AnimationClip swimIdle = BuildSwimIdle();
                AnimationClip swimStroke = BuildSwimStroke();
                AnimationClip jump = BuildJump();
                AnimationClip fall = BuildFall();
                AnimationClip land = BuildLand();
                AnimationClip holdOneHanded = BuildHoldOneHanded();
                AnimationClip holdTwoHanded = BuildHoldTwoHanded();
                AnimationClip useSwing = BuildUseSwing();

                AvatarMask upperBodyMask = BuildUpperBodyMask();
                controller = BuildController(
                    idle, walk, sprint, crouchIdle, crouchWalk, proneIdle, proneCrawl,
                    swimIdle, swimStroke, jump, fall, land,
                    holdOneHanded, holdTwoHanded, useSwing, upperBodyMask);
            }
            finally
            {
                EndSceneSampling(); // restore the rig pose no matter what
            }

            AssetDatabase.SaveAssets();
            AssignToScenePlayer(controller);
            Debug.Log("[PlayerAnimationBuilder] Generated 15 clips, upper-body mask and Animator Controller at " + AnimationsFolder);
        }

        // ------------------------------------------------------------------
        // Muscle name lookup (never hardcode — resolved from HumanTrait)
        // ------------------------------------------------------------------

        private static string legFB_L, legFB_R, kneeL, kneeR, footL, footR;
        private static string armDU_L, armDU_R, armFB_L, armFB_R, forearmL, forearmR;
        private static string spineFB, chestFB, headNod;

        private static void ResolveMuscleNames()
        {
            legFB_L = Muscle("Left Upper Leg", "Front");
            legFB_R = Muscle("Right Upper Leg", "Front");
            kneeL = Muscle("Left Lower Leg", "Stretch");
            kneeR = Muscle("Right Lower Leg", "Stretch");
            footL = Muscle("Left Foot", "Up");
            footR = Muscle("Right Foot", "Up");
            armDU_L = Muscle("Left Arm", "Down");
            armDU_R = Muscle("Right Arm", "Down");
            armFB_L = Muscle("Left Arm", "Front");
            armFB_R = Muscle("Right Arm", "Front");
            forearmL = Muscle("Left Forearm", "Stretch");
            forearmR = Muscle("Right Forearm", "Stretch");
            spineFB = Muscle("Spine", "Front");
            chestFB = Muscle("Chest", "Front");
            headNod = Muscle("Head Nod");
        }

        private static string Muscle(params string[] keywords)
        {
            foreach (string name in HumanTrait.MuscleName)
            {
                bool all = true;
                foreach (string keyword in keywords)
                {
                    if (name.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        all = false;
                        break;
                    }
                }

                if (all)
                    return name;
            }

            Debug.LogWarning("[PlayerAnimationBuilder] Muscle not found for: " + string.Join(" ", keywords));
            return null;
        }

        // ------------------------------------------------------------------
        // Body-height calibration
        //
        // RootT.y is the humanoid BODY (center of mass) height in avatar-scale
        // units — its mapping to actual hips height depends on the avatar's
        // mass distribution, so hardcoded constants are fragile (this caused
        // the sunken-torso / floating-prone bugs). Instead, probe poses are
        // sampled on the scene player's real avatar: two samples give the
        // linear hipsHeight(RootT.y) relation, one extra sample per body pitch
        // gives that pitch's offset. Each clip then solves RootT.y from its
        // desired hips height in meters.
        // ------------------------------------------------------------------

        private static float bodyScale = 0.95f; // fallback if no Player in the scene
        private static readonly Dictionary<float, float> pitchOffsets = new Dictionary<float, float>();

        private static GameObject samplePlayer;
        private static Transform sampleRoot, sampleHips;
        private static Transform[] sampleBones;
        private static Vector3[] savedBonePositions;
        private static Quaternion[] savedBoneRotations;

        /// <summary>
        /// Locates the scene player, snapshots its pose (clip sampling moves the
        /// bones; EndSceneSampling restores them) and calibrates the body-height
        /// mapping.
        /// </summary>
        private static void BeginSceneSampling()
        {
            pitchOffsets.Clear();
            bodyScale = 0.95f;
            samplePlayer = null;

            GameObject player = GameObject.Find("Player");
            Animator animator = player != null ? player.GetComponent<Animator>() : null;
            Transform hips = player != null ? player.transform.Find("Hips") : null;

            if (animator == null || animator.avatar == null || !animator.avatar.isHuman || hips == null)
            {
                Debug.LogWarning("[PlayerAnimationBuilder] No humanoid 'Player' in the open scene — body heights use rough fallbacks. Open the scene containing the player rig and rebuild for exact heights.");
                return;
            }

            samplePlayer = player;
            sampleRoot = player.transform;
            sampleHips = hips;

            sampleBones = player.GetComponentsInChildren<Transform>();
            savedBonePositions = new Vector3[sampleBones.Length];
            savedBoneRotations = new Quaternion[sampleBones.Length];
            for (int i = 0; i < sampleBones.Length; i++)
            {
                savedBonePositions[i] = sampleBones[i].localPosition;
                savedBoneRotations[i] = sampleBones[i].localRotation;
            }

            // Two probes give the linear hipsHeight(RootT.y) relation; one probe
            // per body pitch captures that pitch's offset.
            float y1 = SampleHipsHeight(0.8f, 0f);
            float y2 = SampleHipsHeight(1.3f, 0f);
            float scale = (y2 - y1) / 0.5f;

            if (Mathf.Abs(scale) < 0.05f)
            {
                Debug.LogWarning("[PlayerAnimationBuilder] Body-height calibration produced a degenerate result; using fallbacks.");
                return;
            }

            bodyScale = scale;
            pitchOffsets[0f] = y1 - bodyScale * 0.8f;
            pitchOffsets[SwimIdlePitch] = SampleHipsHeight(0.6f, SwimIdlePitch) - bodyScale * 0.6f;
            pitchOffsets[PronePitch] = SampleHipsHeight(0.6f, PronePitch) - bodyScale * 0.6f;
            pitchOffsets[SwimStrokePitch] = SampleHipsHeight(0.6f, SwimStrokePitch) - bodyScale * 0.6f;

            Debug.Log($"[PlayerAnimationBuilder] Body height calibrated: scale={bodyScale:F3}, upright offset={pitchOffsets[0f]:F3}");

            CalibrateLegBaseline();
        }

        private static void EndSceneSampling()
        {
            if (samplePlayer == null)
                return;

            for (int i = 0; i < sampleBones.Length; i++)
            {
                sampleBones[i].localPosition = savedBonePositions[i];
                sampleBones[i].localRotation = savedBoneRotations[i];
            }

            samplePlayer = null;
            sampleBones = null;
        }

        private static float SampleHipsHeight(float rootT, float pitchDegrees)
        {
            var probe = new AnimationClip { name = "__heightProbe" };
            Add(probe, "RootT.y", Constant(1f, rootT));
            AddBodyPitch(probe, 1f, pitchDegrees);
            probe.SampleAnimation(samplePlayer, 0f);
            Object.DestroyImmediate(probe);
            return sampleHips.position.y - sampleRoot.position.y;
        }

        /// <summary>RootT.y value that puts the hips at the given height (meters) for the given body pitch.</summary>
        private static float RootTForHips(float desiredHipsHeight, float pitchDegrees)
        {
            if (!pitchOffsets.TryGetValue(pitchDegrees, out float offset))
                offset = Mathf.Lerp(-0.28f, 0f, pitchDegrees / 90f); // rough fallback
            return (desiredHipsHeight - offset) / bodyScale;
        }

        // ------------------------------------------------------------------
        // Straight-leg baseline calibration
        //
        // Muscle value 0 is Mecanim's relaxed "muscle neutral", NOT straight
        // legs — knees stay slightly flexed, and with RootT.y pinning the hips
        // the feet end up hovering above the floor. Solve the muscle values
        // that actually make the thigh vertical, the knee straight and the
        // foot level by measuring probe poses on the real avatar; every clip
        // authors its leg curves relative to this baseline.
        // ------------------------------------------------------------------

        private static float legFBBase, kneeBase, footBase;

        private static float LegFB(float offset) => legFBBase + offset;
        private static float Knee(float offset) => kneeBase + offset;
        private static float FootUD(float offset) => footBase + offset;

        private static void CalibrateLegBaseline()
        {
            legFBBase = 0f;
            kneeBase = 0f;
            footBase = 0f;

            if (samplePlayer == null)
                return;

            Animator animator = samplePlayer.GetComponent<Animator>();
            Transform thigh = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            Transform shin = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            Transform foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            if (thigh == null || shin == null || foot == null)
                return;

            // Solve sequentially: knee measurement needs the solved thigh, the
            // foot needs both.
            legFBBase = SolveLegMuscle(v =>
            {
                SampleLegPose(v, 0f, 0f);
                return SignedAngleFromDown(shin.position - thigh.position);
            });
            kneeBase = SolveLegMuscle(v =>
            {
                SampleLegPose(legFBBase, v, 0f);
                return Vector3.SignedAngle(shin.position - thigh.position, foot.position - shin.position, sampleRoot.right);
            });
            footBase = SolveLegMuscle(v =>
            {
                SampleLegPose(legFBBase, kneeBase, v);
                return Vector3.SignedAngle(sampleRoot.forward, foot.forward, sampleRoot.right);
            });

            Debug.Log($"[PlayerAnimationBuilder] Straight-leg baseline: thigh={legFBBase:F3}, knee={kneeBase:F3}, foot={footBase:F3}");
        }

        private static float SignedAngleFromDown(Vector3 direction) =>
            Vector3.SignedAngle(-sampleRoot.up, direction, sampleRoot.right);

        private static void SampleLegPose(float legFB, float knee, float foot)
        {
            var probe = new AnimationClip { name = "__legProbe" };
            Add(probe, "RootT.y", Constant(1f, 1f));
            AddBodyPitch(probe, 1f, 0f);
            Add(probe, legFB_L, Constant(1f, legFB));
            Add(probe, legFB_R, Constant(1f, legFB));
            Add(probe, kneeL, Constant(1f, knee));
            Add(probe, kneeR, Constant(1f, knee));
            Add(probe, footL, Constant(1f, foot));
            Add(probe, footR, Constant(1f, foot));
            probe.SampleAnimation(samplePlayer, 0f);
            Object.DestroyImmediate(probe);
        }

        /// <summary>
        /// Finds the muscle value where the measured angle crosses zero.
        /// Muscle ranges are piecewise linear around 0, so three samples are
        /// probed and the zero is interpolated inside the bracketing segment.
        /// </summary>
        private static float SolveLegMuscle(System.Func<float, float> measureAngle)
        {
            float[] values = { -0.5f, 0f, 0.5f };
            var angles = new float[values.Length];
            for (int i = 0; i < values.Length; i++)
                angles[i] = measureAngle(values[i]);

            for (int i = 0; i < values.Length - 1; i++)
            {
                if (Mathf.Approximately(angles[i], angles[i + 1]))
                    continue;
                float t = angles[i] / (angles[i] - angles[i + 1]);
                if (t >= 0f && t <= 1f)
                    return Mathf.Clamp(values[i] + t * (values[i + 1] - values[i]), -1f, 1f);
            }

            // No crossing inside the probed range: extrapolate from the overall slope.
            float slope = (angles[2] - angles[0]) / (values[2] - values[0]);
            if (Mathf.Approximately(slope, 0f))
                return 0f;
            return Mathf.Clamp(-angles[1] / slope, -1f, 1f);
        }

        // ------------------------------------------------------------------
        // Curve helpers
        // ------------------------------------------------------------------

        private static void Add(AnimationClip clip, string property, AnimationCurve curve)
        {
            if (!string.IsNullOrEmpty(property))
                clip.SetCurve(string.Empty, typeof(Animator), property, curve);
        }

        private static AnimationCurve Constant(float duration, float value)
            => AnimationCurve.Constant(0f, duration, value);

        /// <summary>Sine wave with analytically correct tangents (4 keys per cycle).</summary>
        private static AnimationCurve Sine(float duration, float offset, float amplitude, float phaseDegrees, int cycles = 1)
        {
            int count = cycles * 4 + 1;
            var keys = new Keyframe[count];
            float angularSpeed = 2f * Mathf.PI * cycles / duration;
            float phase = phaseDegrees * Mathf.Deg2Rad;

            for (int i = 0; i < count; i++)
            {
                float t = duration * i / (count - 1);
                float angle = angularSpeed * t + phase;
                float tangent = amplitude * angularSpeed * Mathf.Cos(angle);
                keys[i] = new Keyframe(t, offset + amplitude * Mathf.Sin(angle), tangent, tangent);
            }

            return new AnimationCurve(keys);
        }

        /// <summary>Evenly spaced values across the duration with smoothed tangents.</summary>
        private static AnimationCurve Keys(float duration, params float[] values)
        {
            var curve = new AnimationCurve();
            for (int i = 0; i < values.Length; i++)
                curve.AddKey(new Keyframe(duration * (values.Length == 1 ? 0f : (float)i / (values.Length - 1)), values[i]));
            for (int i = 0; i < curve.length; i++)
                curve.SmoothTangents(i, 0f);
            return curve;
        }

        /// <summary>Constant body pitch via RootQ quaternion curves (prone/swim body orientation).</summary>
        private static void AddBodyPitch(AnimationClip clip, float duration, float pitchDegrees)
        {
            float half = pitchDegrees * 0.5f * Mathf.Deg2Rad;
            Add(clip, "RootQ.x", Constant(duration, Mathf.Sin(half)));
            Add(clip, "RootQ.y", Constant(duration, 0f));
            Add(clip, "RootQ.z", Constant(duration, 0f));
            Add(clip, "RootQ.w", Constant(duration, Mathf.Cos(half)));
        }

        private static void AddRelaxedArms(AnimationClip clip, float duration)
        {
            Add(clip, armDU_L, Constant(duration, ArmsDown));
            Add(clip, armDU_R, Constant(duration, ArmsDown));
            Add(clip, forearmL, Constant(duration, ForearmRelax));
            Add(clip, forearmR, Constant(duration, ForearmRelax));
        }

        private static AnimationClip NewClip(string clipName, bool loop)
        {
            var clip = new AnimationClip { name = clipName, frameRate = 60f };
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        private static AnimationClip Save(AnimationClip clip)
        {
            AssetDatabase.CreateAsset(clip, AnimationsFolder + "/" + clip.name + ".anim");
            return clip;
        }

        // ------------------------------------------------------------------
        // Clips
        // ------------------------------------------------------------------

        // --- Upper-body layer clips (Phase 9 — item holding) ----------------
        // These play on the arms-only override layer, so they author ONLY arm
        // muscles: no RootT/RootQ/leg/spine curves (the mask ignores them and
        // the base layer must keep owning body height and stance).

        /// <summary>Right arm carries the item raised and bent; left arm stays relaxed (the base layer swings it while walking).</summary>
        private static AnimationClip BuildHoldOneHanded()
        {
            const float d = 2f;
            AnimationClip clip = NewClip("HoldOneHanded", true);
            Add(clip, armDU_L, Constant(d, ArmsDown));
            Add(clip, forearmL, Constant(d, ForearmRelax));
            Add(clip, armDU_R, Constant(d, ArmsDown + 0.35f));
            Add(clip, armFB_R, Sine(d, 0.25f, 0.02f, 0f)); // tiny sway so the hold breathes
            Add(clip, forearmR, Constant(d, 0.55f));
            return Save(clip);
        }

        /// <summary>Both arms raised forward gripping; the off-hand lands exactly on the item via the bone-based IK pass.</summary>
        private static AnimationClip BuildHoldTwoHanded()
        {
            const float d = 2f;
            AnimationClip clip = NewClip("HoldTwoHanded", true);
            Add(clip, armDU_R, Constant(d, ArmsDown + 0.35f));
            Add(clip, armFB_R, Sine(d, 0.3f, 0.02f, 0f));
            Add(clip, forearmR, Constant(d, 0.55f));
            Add(clip, armDU_L, Constant(d, ArmsDown + 0.4f));
            Add(clip, armFB_L, Sine(d, 0.28f, 0.02f, 90f));
            Add(clip, forearmL, Constant(d, 0.5f));
            return Save(clip);
        }

        /// <summary>Generic swing (mine/chop/attack): wind up, strike through, recover. Non-looping; the Use trigger fires it.</summary>
        private static AnimationClip BuildUseSwing()
        {
            const float d = 0.45f;
            AnimationClip clip = NewClip("UseSwing", false);
            Add(clip, armFB_R, Keys(d, 0.3f, 0.75f, -0.15f, 0.2f));
            Add(clip, armDU_R, Keys(d, ArmsDown + 0.35f, ArmsDown + 0.5f, ArmsDown + 0.15f, ArmsDown + 0.32f));
            Add(clip, forearmR, Keys(d, 0.55f, 0.75f, 0.2f, 0.5f));
            Add(clip, armFB_L, Keys(d, 0.15f, 0.3f, 0f, 0.12f)); // mild follow — reads on two-handed, harmless on one-handed
            Add(clip, forearmL, Keys(d, 0.4f, 0.5f, 0.25f, 0.4f));
            return Save(clip);
        }

        private static AnimationClip BuildIdle()
        {
            const float d = 3f;
            AnimationClip clip = NewClip("Idle", true);
            Add(clip, "RootT.y", Constant(d, RootTForHips(StandHipsHeight, 0f)));
            AddBodyPitch(clip, d, 0f);
            AddRelaxedArms(clip, d);
            Add(clip, legFB_L, Constant(d, LegFB(0f)));          // explicit straight legs —
            Add(clip, legFB_R, Constant(d, LegFB(0f)));          // muscle 0 leaves knees flexed
            Add(clip, kneeL, Constant(d, Knee(0f)));
            Add(clip, kneeR, Constant(d, Knee(0f)));
            Add(clip, footL, Constant(d, FootUD(0f)));
            Add(clip, footR, Constant(d, FootUD(0f)));
            Add(clip, spineFB, Sine(d, 0f, 0.03f, 0f));          // breathing
            Add(clip, chestFB, Sine(d, 0f, 0.02f, 90f));
            return Save(clip);
        }

        private static AnimationClip BuildWalk()
        {
            const float d = 1f;
            AnimationClip clip = NewClip("Walk", true);
            Add(clip, "RootT.y", Constant(d, RootTForHips(StandHipsHeight, 0f)));
            AddBodyPitch(clip, d, 0f);
            Add(clip, legFB_L, Sine(d, LegFB(0f), WalkLegSwing, 0f));
            Add(clip, legFB_R, Sine(d, LegFB(0f), WalkLegSwing, 180f));
            Add(clip, kneeL, Sine(d, Knee(-0.25f), 0.25f, 90f)); // knee folds during its swing, straightens at the extremes
            Add(clip, kneeR, Sine(d, Knee(-0.25f), 0.25f, 270f));
            Add(clip, footL, Sine(d, FootUD(0f), 0.15f, 45f));
            Add(clip, footR, Sine(d, FootUD(0f), 0.15f, 225f));
            Add(clip, armFB_L, Sine(d, 0f, WalkArmSwing, 180f)); // arms counter-swing the legs
            Add(clip, armFB_R, Sine(d, 0f, WalkArmSwing, 0f));
            AddRelaxedArms(clip, d);
            Add(clip, spineFB, Sine(d, -0.02f, 0.02f, 0f, 2));
            return Save(clip);
        }

        private static AnimationClip BuildSprint()
        {
            const float d = 0.7f;
            AnimationClip clip = NewClip("Sprint", true);
            Add(clip, "RootT.y", Constant(d, RootTForHips(StandHipsHeight, 0f)));
            AddBodyPitch(clip, d, 0f);
            Add(clip, legFB_L, Sine(d, LegFB(0f), SprintLegSwing, 0f));
            Add(clip, legFB_R, Sine(d, LegFB(0f), SprintLegSwing, 180f));
            Add(clip, kneeL, Sine(d, Knee(-0.35f), 0.35f, 90f));
            Add(clip, kneeR, Sine(d, Knee(-0.35f), 0.35f, 270f));
            Add(clip, footL, Sine(d, FootUD(0f), 0.2f, 45f));
            Add(clip, footR, Sine(d, FootUD(0f), 0.2f, 225f));
            Add(clip, armFB_L, Sine(d, 0f, SprintArmSwing, 180f));
            Add(clip, armFB_R, Sine(d, 0f, SprintArmSwing, 0f));
            Add(clip, armDU_L, Constant(d, ArmsDown + 0.1f));
            Add(clip, armDU_R, Constant(d, ArmsDown + 0.1f));
            Add(clip, forearmL, Constant(d, 0.5f));              // elbows pumped
            Add(clip, forearmR, Constant(d, 0.5f));
            Add(clip, spineFB, Constant(d, SprintLean));
            return Save(clip);
        }

        private static AnimationClip BuildCrouchIdle()
        {
            const float d = 2f;
            AnimationClip clip = NewClip("CrouchIdle", true);
            Add(clip, "RootT.y", Constant(d, RootTForHips(CrouchHipsHeight, 0f)));
            AddBodyPitch(clip, d, 0f);
            Add(clip, legFB_L, Constant(d, LegFB(CrouchThighBend)));
            Add(clip, legFB_R, Constant(d, LegFB(CrouchThighBend)));
            Add(clip, kneeL, Constant(d, Knee(CrouchKneeBend)));
            Add(clip, kneeR, Constant(d, Knee(CrouchKneeBend)));
            Add(clip, footL, Constant(d, FootUD(0.2f)));
            Add(clip, footR, Constant(d, FootUD(0.2f)));
            Add(clip, spineFB, Sine(d, -0.15f, 0.02f, 0f));      // hunched, breathing
            AddRelaxedArms(clip, d);
            return Save(clip);
        }

        private static AnimationClip BuildCrouchWalk()
        {
            const float d = 1.1f;
            AnimationClip clip = NewClip("CrouchWalk", true);
            Add(clip, "RootT.y", Constant(d, RootTForHips(CrouchHipsHeight, 0f))); // constant: bobbing fights the IK pelvis correction
            AddBodyPitch(clip, d, 0f);
            Add(clip, legFB_L, Sine(d, LegFB(CrouchThighBend), 0.25f, 0f));
            Add(clip, legFB_R, Sine(d, LegFB(CrouchThighBend), 0.25f, 180f));
            Add(clip, kneeL, Sine(d, Knee(CrouchKneeBend), 0.15f, 90f));
            Add(clip, kneeR, Sine(d, Knee(CrouchKneeBend), 0.15f, 270f));
            Add(clip, footL, Sine(d, FootUD(0.2f), 0.1f, 45f));
            Add(clip, footR, Sine(d, FootUD(0.2f), 0.1f, 225f));
            Add(clip, armFB_L, Sine(d, 0f, 0.15f, 180f));
            Add(clip, armFB_R, Sine(d, 0f, 0.15f, 0f));
            Add(clip, spineFB, Constant(d, -0.15f));
            AddRelaxedArms(clip, d);
            return Save(clip);
        }

        private static AnimationClip BuildProneIdle()
        {
            const float d = 3f;
            AnimationClip clip = NewClip("ProneIdle", true);
            Add(clip, "RootT.y", Constant(d, RootTForHips(ProneHipsHeight, PronePitch)));
            AddBodyPitch(clip, d, PronePitch);
            Add(clip, legFB_L, Constant(d, LegFB(0.05f)));
            Add(clip, legFB_R, Constant(d, LegFB(0.05f)));
            Add(clip, kneeL, Constant(d, Knee(-0.1f)));
            Add(clip, kneeR, Constant(d, Knee(-0.1f)));
            Add(clip, armDU_L, Constant(d, -0.7f));
            Add(clip, armDU_R, Constant(d, -0.7f));
            Add(clip, armFB_L, Constant(d, -0.3f));
            Add(clip, armFB_R, Constant(d, -0.3f));
            Add(clip, forearmL, Constant(d, -0.4f));             // forearms braced under the chest
            Add(clip, forearmR, Constant(d, -0.4f));
            Add(clip, headNod, Constant(d, 0.5f));               // eyes to the horizon, not the dirt
            Add(clip, spineFB, Sine(d, 0f, 0.02f, 0f));
            return Save(clip);
        }

        private static AnimationClip BuildProneCrawl()
        {
            const float d = 1.6f;
            AnimationClip clip = NewClip("ProneCrawl", true);
            Add(clip, "RootT.y", Constant(d, RootTForHips(ProneHipsHeight, PronePitch)));
            AddBodyPitch(clip, d, PronePitch);
            Add(clip, legFB_L, Sine(d, LegFB(0.05f), 0.25f, 0f));
            Add(clip, legFB_R, Sine(d, LegFB(0.05f), 0.25f, 180f));
            Add(clip, kneeL, Sine(d, Knee(-0.25f), 0.2f, 90f));
            Add(clip, kneeR, Sine(d, Knee(-0.25f), 0.2f, 270f));
            Add(clip, armDU_L, Constant(d, -0.7f));
            Add(clip, armDU_R, Constant(d, -0.7f));
            Add(clip, armFB_L, Sine(d, -0.3f, 0.35f, 180f));     // opposite arm/leg crawl pattern
            Add(clip, armFB_R, Sine(d, -0.3f, 0.35f, 0f));
            Add(clip, forearmL, Sine(d, -0.4f, 0.2f, 90f));
            Add(clip, forearmR, Sine(d, -0.4f, 0.2f, 270f));
            Add(clip, headNod, Constant(d, 0.5f));
            return Save(clip);
        }

        private static AnimationClip BuildSwimIdle()
        {
            const float d = 2.5f;
            AnimationClip clip = NewClip("SwimIdle", true);
            Add(clip, "RootT.y", Sine(d, RootTForHips(SwimIdleHipsHeight, SwimIdlePitch), 0.02f, 0f));
            AddBodyPitch(clip, d, SwimIdlePitch);
            Add(clip, legFB_L, Sine(d, LegFB(0f), 0.2f, 0f, 2)); // treading kicks
            Add(clip, legFB_R, Sine(d, LegFB(0f), 0.2f, 180f, 2));
            Add(clip, kneeL, Sine(d, Knee(-0.3f), 0.15f, 90f, 2));
            Add(clip, kneeR, Sine(d, Knee(-0.3f), 0.15f, 270f, 2));
            Add(clip, armDU_L, Sine(d, -0.3f, 0.2f, 0f));        // gentle sculling sweep
            Add(clip, armDU_R, Sine(d, -0.3f, 0.2f, 180f));
            Add(clip, forearmL, Constant(d, -0.3f));
            Add(clip, forearmR, Constant(d, -0.3f));
            return Save(clip);
        }

        private static AnimationClip BuildSwimStroke()
        {
            const float d = 1.2f;
            AnimationClip clip = NewClip("SwimStroke", true);
            Add(clip, "RootT.y", Constant(d, RootTForHips(SwimStrokeHipsHeight, SwimStrokePitch)));
            AddBodyPitch(clip, d, SwimStrokePitch);
            Add(clip, legFB_L, Sine(d, LegFB(0f), 0.3f, 0f, 2)); // flutter kick
            Add(clip, legFB_R, Sine(d, LegFB(0f), 0.3f, 180f, 2));
            Add(clip, kneeL, Sine(d, Knee(-0.15f), 0.15f, 90f, 2));
            Add(clip, kneeR, Sine(d, Knee(-0.15f), 0.15f, 270f, 2));
            Add(clip, armFB_L, Sine(d, 0f, 0.5f, 0f));           // stroke sweep
            Add(clip, armFB_R, Sine(d, 0f, 0.5f, 180f));
            Add(clip, armDU_L, Sine(d, -0.2f, 0.25f, 90f));
            Add(clip, armDU_R, Sine(d, -0.2f, 0.25f, 270f));
            Add(clip, forearmL, Sine(d, -0.2f, 0.2f, 0f));
            Add(clip, forearmR, Sine(d, -0.2f, 0.2f, 180f));
            Add(clip, headNod, Constant(d, 0.4f));
            return Save(clip);
        }

        private static AnimationClip BuildJump()
        {
            const float d = 0.4f;
            AnimationClip clip = NewClip("Jump", false);
            Add(clip, "RootT.y", Constant(d, RootTForHips(StandHipsHeight, 0f))); // vertical motion is physical, not animated
            AddBodyPitch(clip, d, 0f);
            Add(clip, legFB_L, Keys(d, LegFB(0f), LegFB(-0.3f), LegFB(0.1f))); // tuck then extend
            Add(clip, legFB_R, Keys(d, LegFB(0f), LegFB(-0.3f), LegFB(0.1f)));
            Add(clip, kneeL, Keys(d, Knee(-0.1f), Knee(-0.6f), Knee(-0.2f)));
            Add(clip, kneeR, Keys(d, Knee(-0.1f), Knee(-0.6f), Knee(-0.2f)));
            Add(clip, armDU_L, Keys(d, ArmsDown, -0.1f, -0.2f)); // arms drive upward
            Add(clip, armDU_R, Keys(d, ArmsDown, -0.1f, -0.2f));
            Add(clip, spineFB, Keys(d, 0f, 0.05f, 0f));
            return Save(clip);
        }

        private static AnimationClip BuildFall()
        {
            const float d = 1.2f;
            AnimationClip clip = NewClip("Fall", true);
            Add(clip, "RootT.y", Constant(d, RootTForHips(StandHipsHeight, 0f)));
            AddBodyPitch(clip, d, 0f);
            Add(clip, legFB_L, Sine(d, LegFB(-0.15f), 0.08f, 0f)); // loose, slightly bent limbs
            Add(clip, legFB_R, Sine(d, LegFB(-0.15f), 0.08f, 180f));
            Add(clip, kneeL, Constant(d, Knee(-0.35f)));
            Add(clip, kneeR, Constant(d, Knee(-0.35f)));
            Add(clip, armDU_L, Constant(d, -0.15f));             // arms out for balance
            Add(clip, armDU_R, Constant(d, -0.15f));
            Add(clip, armFB_L, Sine(d, 0f, 0.15f, 0f));
            Add(clip, armFB_R, Sine(d, 0f, 0.15f, 180f));
            return Save(clip);
        }

        private static AnimationClip BuildLand()
        {
            const float d = 0.35f;
            AnimationClip clip = NewClip("Land", false);
            Add(clip, "RootT.y", Keys(d,                       // impact dip in real meters
                RootTForHips(StandHipsHeight, 0f),
                RootTForHips(StandHipsHeight - 0.18f, 0f),
                RootTForHips(StandHipsHeight - 0.04f, 0f)));
            AddBodyPitch(clip, d, 0f);
            Add(clip, legFB_L, Keys(d, LegFB(0f), LegFB(-0.3f), LegFB(-0.05f)));
            Add(clip, legFB_R, Keys(d, LegFB(0f), LegFB(-0.3f), LegFB(-0.05f)));
            Add(clip, kneeL, Keys(d, Knee(0f), Knee(-0.55f), Knee(-0.15f)));
            Add(clip, kneeR, Keys(d, Knee(0f), Knee(-0.55f), Knee(-0.15f)));
            Add(clip, spineFB, Keys(d, 0f, -0.2f, -0.05f));
            AddRelaxedArms(clip, d);
            return Save(clip);
        }

        // ------------------------------------------------------------------
        // Mask + controller
        // ------------------------------------------------------------------

        private static AvatarMask BuildUpperBodyMask()
        {
            var mask = new AvatarMask { name = "UpperBodyMask" };
            for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
                mask.SetHumanoidBodyPartActive(part, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftHandIK, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightHandIK, true);
            AssetDatabase.CreateAsset(mask, UpperBodyMaskPath);
            return mask;
        }

        private static AnimatorController BuildController(
            AnimationClip idle, AnimationClip walk, AnimationClip sprint,
            AnimationClip crouchIdle, AnimationClip crouchWalk,
            AnimationClip proneIdle, AnimationClip proneCrawl,
            AnimationClip swimIdle, AnimationClip swimStroke,
            AnimationClip jump, AnimationClip fall, AnimationClip land,
            AnimationClip holdOneHanded, AnimationClip holdTwoHanded, AnimationClip useSwing,
            AvatarMask upperBodyMask)
        {
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
            controller.AddParameter("Speed01", AnimatorControllerParameterType.Float);
            controller.AddParameter("VerticalVelocity", AnimatorControllerParameterType.Float);
            controller.AddParameter("IsCrouching", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsProne", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsSwimming", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsGrounded", AnimatorControllerParameterType.Bool);

            // Upper-body layer parameters (Phase 9): 0 = empty hands,
            // 1 = one-handed/off-hand pose, 2 = two-handed pose. ItemHoldController drives them.
            controller.AddParameter("HoldPose", AnimatorControllerParameterType.Int);
            controller.AddParameter("Use", AnimatorControllerParameterType.Trigger);

            // Base layer: rename + enable the IK pass for foot planting.
            AnimatorControllerLayer[] layers = controller.layers;
            layers[0].name = "Base";
            layers[0].iKPass = true;
            controller.layers = layers;

            AnimatorStateMachine baseMachine = controller.layers[0].stateMachine;

            // --- Locomotion: 2D strafe blend (Idle -> Walk -> Sprint) ---------
            AnimatorState locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree locomotionTree, 0);
            locomotionTree.blendType = BlendTreeType.FreeformDirectional2D;
            locomotionTree.blendParameter = "MoveX";
            locomotionTree.blendParameterY = "MoveZ";
            locomotionTree.AddChild(idle, Vector2.zero);
            locomotionTree.AddChild(walk, new Vector2(0f, 1f));
            locomotionTree.AddChild(walk, new Vector2(0f, -0.75f));  // backpedal caps at 0.75x speed
            locomotionTree.AddChild(walk, new Vector2(-0.9f, 0f));
            locomotionTree.AddChild(walk, new Vector2(0.9f, 0f));
            locomotionTree.AddChild(sprint, new Vector2(0f, 2f));    // sprintSpeed / walkSpeed

            AnimatorState crouch = Create1DState(controller, "Crouch", crouchIdle, crouchWalk);
            AnimatorState prone = Create1DState(controller, "Prone", proneIdle, proneCrawl);
            AnimatorState swim = Create1DState(controller, "Swim", swimIdle, swimStroke);
            AnimatorState jumpState = baseMachine.AddState("Jump");
            jumpState.motion = jump;
            AnimatorState fallState = baseMachine.AddState("Fall");
            fallState.motion = fall;
            AnimatorState landState = baseMachine.AddState("Land");
            landState.motion = land;

            baseMachine.defaultState = locomotion;

            // --- Transitions ---------------------------------------------------
            // Locomotion <-> Crouch
            Transition(locomotion, crouch, 0.25f).AddCondition(AnimatorConditionMode.If, 0f, "IsCrouching");
            var crouchToLoco = Transition(crouch, locomotion, 0.25f);
            crouchToLoco.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsCrouching");
            crouchToLoco.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsProne");

            // Crouch <-> Prone, plus direct Locomotion -> Prone: gameplay allows
            // going prone straight from Idle/Walking without passing Crouching.
            Transition(crouch, prone, 0.35f).AddCondition(AnimatorConditionMode.If, 0f, "IsProne");
            Transition(prone, crouch, 0.35f).AddCondition(AnimatorConditionMode.IfNot, 0f, "IsProne");
            Transition(locomotion, prone, 0.35f).AddCondition(AnimatorConditionMode.If, 0f, "IsProne");

            // Airborne
            var locoToJump = Transition(locomotion, jumpState, 0.05f);
            locoToJump.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsGrounded");
            locoToJump.AddCondition(AnimatorConditionMode.Greater, 0.5f, "VerticalVelocity");
            var locoToFall = Transition(locomotion, fallState, 0.25f);
            locoToFall.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsGrounded");
            locoToFall.AddCondition(AnimatorConditionMode.Less, 0f, "VerticalVelocity");
            var crouchToFall = Transition(crouch, fallState, 0.25f);
            crouchToFall.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsGrounded");
            crouchToFall.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsSwimming");

            Transition(jumpState, fallState, 0.3f).AddCondition(AnimatorConditionMode.Less, -0.1f, "VerticalVelocity");
            Transition(jumpState, landState, 0.1f).AddCondition(AnimatorConditionMode.If, 0f, "IsGrounded");
            Transition(fallState, landState, 0.08f).AddCondition(AnimatorConditionMode.If, 0f, "IsGrounded");

            // Land blends back to ground states after most of the clip played.
            var landToLoco = Transition(landState, locomotion, 0.2f);
            landToLoco.hasExitTime = true;
            landToLoco.exitTime = 0.6f;
            landToLoco.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsCrouching");
            var landToCrouch = Transition(landState, crouch, 0.2f);
            landToCrouch.hasExitTime = true;
            landToCrouch.exitTime = 0.6f;
            landToCrouch.AddCondition(AnimatorConditionMode.If, 0f, "IsCrouching");
            Transition(landState, fallState, 0.15f).AddCondition(AnimatorConditionMode.IfNot, 0f, "IsGrounded");

            // Swimming: entered from anywhere (falling in, wading deep, etc.)
            var anyToSwim = baseMachine.AddAnyStateTransition(swim);
            anyToSwim.hasExitTime = false;
            anyToSwim.hasFixedDuration = true;
            anyToSwim.duration = 0.4f;
            anyToSwim.canTransitionToSelf = false;
            anyToSwim.AddCondition(AnimatorConditionMode.If, 0f, "IsSwimming");

            var swimToLoco = Transition(swim, locomotion, 0.3f);
            swimToLoco.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsSwimming");
            swimToLoco.AddCondition(AnimatorConditionMode.If, 0f, "IsGrounded");
            var swimToFall = Transition(swim, fallState, 0.3f);
            swimToFall.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsSwimming");
            swimToFall.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsGrounded");

            // --- Upper-body override layer: item hold poses + Use swing --------
            // (The layer reserved since the movement phases, now populated.)
            // ItemHoldController drives HoldPose/Use and the layer WEIGHT — the
            // layer stays at 0 with empty hands, so the base layer's arm swing
            // survives unarmed play untouched.
            var upperBodyMachine = new AnimatorStateMachine
            {
                name = "UpperBody",
                hideFlags = HideFlags.HideInHierarchy
            };
            AssetDatabase.AddObjectToAsset(upperBodyMachine, controller);

            AnimatorState emptyState = upperBodyMachine.AddState("Empty"); // no motion
            upperBodyMachine.defaultState = emptyState;

            AnimatorState oneHandedState = upperBodyMachine.AddState("OneHandedHold");
            oneHandedState.motion = holdOneHanded;
            AnimatorState twoHandedState = upperBodyMachine.AddState("TwoHandedHold");
            twoHandedState.motion = holdTwoHanded;
            AnimatorState useState = upperBodyMachine.AddState("Use");
            useState.motion = useSwing;

            // Pose selection by HoldPose int — short crossfades keep equipping
            // as responsive as the rest of the character.
            Transition(emptyState, oneHandedState, 0.15f).AddCondition(AnimatorConditionMode.Equals, 1f, "HoldPose");
            Transition(emptyState, twoHandedState, 0.15f).AddCondition(AnimatorConditionMode.Equals, 2f, "HoldPose");
            Transition(oneHandedState, emptyState, 0.2f).AddCondition(AnimatorConditionMode.Equals, 0f, "HoldPose");
            Transition(oneHandedState, twoHandedState, 0.2f).AddCondition(AnimatorConditionMode.Equals, 2f, "HoldPose");
            Transition(twoHandedState, emptyState, 0.2f).AddCondition(AnimatorConditionMode.Equals, 0f, "HoldPose");
            Transition(twoHandedState, oneHandedState, 0.2f).AddCondition(AnimatorConditionMode.Equals, 1f, "HoldPose");

            // Generic Use swing from any pose; self-transition allowed so rapid
            // swings restart the strike instead of queuing.
            AnimatorStateTransition anyToUse = upperBodyMachine.AddAnyStateTransition(useState);
            anyToUse.hasExitTime = false;
            anyToUse.hasFixedDuration = true;
            anyToUse.duration = 0.08f;
            anyToUse.canTransitionToSelf = true;
            anyToUse.AddCondition(AnimatorConditionMode.If, 0f, "Use");

            // Back to the matching hold pose near the end of the strike.
            AnimatorStateTransition useToOne = Transition(useState, oneHandedState, 0.15f);
            useToOne.hasExitTime = true;
            useToOne.exitTime = 0.85f;
            useToOne.AddCondition(AnimatorConditionMode.Equals, 1f, "HoldPose");
            AnimatorStateTransition useToTwo = Transition(useState, twoHandedState, 0.15f);
            useToTwo.hasExitTime = true;
            useToTwo.exitTime = 0.85f;
            useToTwo.AddCondition(AnimatorConditionMode.Equals, 2f, "HoldPose");
            AnimatorStateTransition useToEmpty = Transition(useState, emptyState, 0.15f);
            useToEmpty.hasExitTime = true;
            useToEmpty.exitTime = 0.85f;
            useToEmpty.AddCondition(AnimatorConditionMode.Equals, 0f, "HoldPose");

            controller.AddLayer(new AnimatorControllerLayer
            {
                name = "UpperBody",
                avatarMask = upperBodyMask,
                defaultWeight = 0f, // ItemHoldController raises this while holding
                blendingMode = AnimatorLayerBlendingMode.Override,
                stateMachine = upperBodyMachine
            });

            return controller;
        }

        private static AnimatorState Create1DState(AnimatorController controller, string stateName, AnimationClip idleClip, AnimationClip moveClip)
        {
            AnimatorState state = controller.CreateBlendTreeInController(stateName, out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed01";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idleClip, 0f);
            tree.AddChild(moveClip, 1f);
            return state;
        }

        private static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to, float duration)
        {
            AnimatorStateTransition transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = duration;
            return transition;
        }

        private static void AssignToScenePlayer(AnimatorController controller)
        {
            GameObject player = GameObject.Find("Player");
            if (player == null)
            {
                Debug.LogWarning("[PlayerAnimationBuilder] No 'Player' in the open scene — assign the controller to its Animator manually.");
                return;
            }

            var animator = player.GetComponent<Animator>();
            animator.runtimeAnimatorController = controller;

            if (player.GetComponent<PlayerAnimationController>() == null)
                player.AddComponent<PlayerAnimationController>();

            var references = player.GetComponent<PlayerReferences>();
            if (references != null)
                references.ResolveReferences();

            EditorUtility.SetDirty(player);
            Debug.Log("[PlayerAnimationBuilder] Controller assigned to scene Player.", player);
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
