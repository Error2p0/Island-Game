using UnityEngine;

namespace IslandGame.Player
{
    /// <summary>
    /// Bone-based foot grounding. Runs in LateUpdate, AFTER the Animator has
    /// written the skeleton, and works purely on bone transforms — it never
    /// touches Mecanim's IK goals (whose baked-curve conventions proved
    /// unreliable for generated clips).
    ///
    /// Per leg, per frame:
    ///   1. Read the real animated ankle position from the foot bone.
    ///   2. Raycast for ground. If the terrain is ABOVE the foot (step, bump,
    ///      uphill), lift the foot onto it — LIFT-ONLY, so a walk cycle's
    ///      swing foot is never pulled down.
    ///   3. Move the ankle there with an analytic two-bone IK solve on the
    ///      thigh/shin bones (knee poled forward).
    ///   4. Restore the foot's animated world rotation, optionally tilted
    ///      partway to the surface normal.
    /// On flat ground the correction is zero and the animation shows through
    /// untouched.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerFootGrounder : MonoBehaviour
    {
        [SerializeField] private bool enableGrounding = true;

        [Tooltip("Ankle-bone height above the sole; the ankle is placed this far above the ground hit.")]
        [SerializeField] private float ankleHeight = 0.11f;

        [Tooltip("Measure Ankle Height from the foot visuals' actual renderer bounds at startup, so planted soles touch the ground instead of hovering on a hand-tuned guess. Disable to use the value above verbatim.")]
        [SerializeField] private bool autoCalibrateAnkleHeight = true;

        [Tooltip("Ground ray starts this far above the ankle.")]
        [SerializeField] private float rayUp = 0.5f;

        [Tooltip("Ground ray length below the start point.")]
        [SerializeField] private float rayDown = 1.0f;

        [Tooltip("Maximum lift applied to a foot, meters.")]
        [SerializeField] private float maxLift = 0.4f;

        [Tooltip("How fast a foot's lift blends in/out, meters per second. Keeps plant/release smooth.")]
        [SerializeField] private float liftBlendSpeed = 3f;

        [Tooltip("0 = planted feet keep their animated rotation; 1 = fully tilted to the ground surface.")]
        [Range(0f, 1f)]
        [SerializeField] private float slopeFootTilt = 0.5f;

        [Tooltip("Layers feet plant on. Rays start at ankle height inside the player's own collider, so it can't be hit; triggers are ignored.")]
        [SerializeField] private LayerMask groundMask = ~0;

        private sealed class Leg
        {
            public Transform upper;   // hip joint
            public Transform lower;   // knee
            public Transform foot;    // ankle
            public float lift;        // smoothed current lift in meters
        }

        private PlayerReferences references;
        private Animator animator;
        private Leg leftLeg;
        private Leg rightLeg;

        private void Awake()
        {
            references = GetComponent<PlayerReferences>();
            animator = GetComponent<Animator>();
            leftLeg = ResolveLeg(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
                "LeftUpperLeg", "LeftLowerLeg", "LeftFoot");
            rightLeg = ResolveLeg(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
                "RightUpperLeg", "RightLowerLeg", "RightFoot");

            if (autoCalibrateAnkleHeight)
            {
                // Awake runs before the Animator ever poses the rig, so the
                // bones still hold the authored rest pose — measure the real
                // sole-to-ankle distance from the foot visuals there.
                float measured = MeasureAnkleHeight();
                if (measured > 0.01f)
                    ankleHeight = measured;
            }
        }

        private float MeasureAnkleHeight()
        {
            float total = 0f;
            int measured = 0;

            foreach (Leg leg in new[] { leftLeg, rightLeg })
            {
                if (leg == null)
                    continue;

                float lowestPoint = float.MaxValue;
                foreach (Renderer footRenderer in leg.foot.GetComponentsInChildren<Renderer>())
                    lowestPoint = Mathf.Min(lowestPoint, footRenderer.bounds.min.y);

                if (lowestPoint == float.MaxValue)
                    continue;

                total += leg.foot.position.y - lowestPoint;
                measured++;
            }

            return measured > 0 ? total / measured : 0f;
        }

        private void LateUpdate()
        {
            PlayerState state = references.StateMachine.CurrentState;

            // No grounding without ground contact, in water, or lying down
            // (a prone body drags its feet; planting fights the pose).
            bool active = enableGrounding
                          && leftLeg != null && rightLeg != null
                          && references.Locomotion.IsEffectivelyGrounded
                          && state != PlayerState.Swimming
                          && state != PlayerState.Prone;

            GroundLeg(leftLeg, active);
            GroundLeg(rightLeg, active);
        }

        private void GroundLeg(Leg leg, bool active)
        {
            Vector3 ankle = leg.foot.position;
            float desiredLift = 0f;
            bool hasSurface = false;
            Vector3 surfaceNormal = Vector3.up;

            if (active && Physics.Raycast(ankle + Vector3.up * rayUp, Vector3.down, out RaycastHit hit,
                    rayUp + rayDown, groundMask, QueryTriggerInteraction.Ignore))
            {
                float correction = hit.point.y + ankleHeight - ankle.y;
                if (correction > 0.01f)
                {
                    desiredLift = Mathf.Min(correction, maxLift);
                    hasSurface = true;
                    surfaceNormal = hit.normal;
                }
            }

            leg.lift = Mathf.MoveTowards(leg.lift, desiredLift, liftBlendSpeed * Time.deltaTime);
            if (leg.lift <= 0.0005f)
                return;

            Quaternion animatedFootRotation = leg.foot.rotation;

            SolveLeg(leg, ankle + Vector3.up * leg.lift);

            // The solve rotated the shin, dragging the foot's rotation with it —
            // restore the animated orientation, optionally tilted to the slope.
            if (hasSurface && slopeFootTilt > 0f)
            {
                Quaternion tilted = Quaternion.FromToRotation(Vector3.up, surfaceNormal) * animatedFootRotation;
                leg.foot.rotation = Quaternion.Slerp(animatedFootRotation, tilted, slopeFootTilt);
            }
            else
            {
                leg.foot.rotation = animatedFootRotation;
            }
        }

        /// <summary>
        /// Analytic two-bone IK: places the ankle at the target by rotating the
        /// thigh and shin bones. Law of cosines gives the hip angle; the knee is
        /// poled toward the character's forward. Applied as delta rotations, so
        /// the animated twist of the bones is preserved.
        /// </summary>
        private void SolveLeg(Leg leg, Vector3 targetAnkle)
        {
            Vector3 hip = leg.upper.position;
            Vector3 knee = leg.lower.position;
            Vector3 ankle = leg.foot.position;

            float thighLength = Vector3.Distance(hip, knee);
            float shinLength = Vector3.Distance(knee, ankle);

            Vector3 toTarget = targetAnkle - hip;
            float distance = Mathf.Clamp(toTarget.magnitude, 0.02f, thighLength + shinLength - 0.001f);
            if (toTarget.sqrMagnitude < 1e-6f)
                return;
            Vector3 direction = toTarget.normalized;

            // Axis that swings the thigh toward the knee-forward pole.
            Vector3 bendAxis = Vector3.Cross(direction, transform.forward);
            if (bendAxis.sqrMagnitude < 1e-6f)
                return; // degenerate (leg pointing straight along forward) — skip this frame
            bendAxis.Normalize();

            float cosHip = (thighLength * thighLength + distance * distance - shinLength * shinLength)
                           / (2f * thighLength * distance);
            float hipAngle = Mathf.Acos(Mathf.Clamp(cosHip, -1f, 1f)) * Mathf.Rad2Deg;

            Vector3 kneeTarget = hip + Quaternion.AngleAxis(hipAngle, bendAxis) * direction * thighLength;

            leg.upper.rotation = Quaternion.FromToRotation(knee - hip, kneeTarget - hip) * leg.upper.rotation;

            // Thigh moved the knee and ankle; aim the shin from the NEW knee.
            Vector3 newKnee = leg.lower.position;
            Vector3 newAnkle = leg.foot.position;
            leg.lower.rotation = Quaternion.FromToRotation(newAnkle - newKnee, targetAnkle - newKnee) * leg.lower.rotation;
        }

        private Leg ResolveLeg(HumanBodyBones upperBone, HumanBodyBones lowerBone, HumanBodyBones footBone,
            string upperName, string lowerName, string footName)
        {
            Transform upper = animator != null && animator.avatar != null ? animator.GetBoneTransform(upperBone) : null;
            Transform lower = animator != null && animator.avatar != null ? animator.GetBoneTransform(lowerBone) : null;
            Transform foot = animator != null && animator.avatar != null ? animator.GetBoneTransform(footBone) : null;

            if (upper == null) upper = FindDeep(transform, upperName);
            if (lower == null) lower = FindDeep(transform, lowerName);
            if (foot == null) foot = FindDeep(transform, footName);

            if (upper == null || lower == null || foot == null)
            {
                Debug.LogError($"[PlayerFootGrounder] Could not resolve leg bones ({upperName}); grounding disabled.", this);
                return null;
            }

            return new Leg { upper = upper, lower = lower, foot = foot };
        }

        private static Transform FindDeep(Transform parent, string childName)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == childName)
                    return child;
                Transform found = FindDeep(child, childName);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}
