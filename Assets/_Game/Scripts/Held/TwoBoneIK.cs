using UnityEngine;

namespace IslandGame.Held
{
    /// <summary>
    /// Analytic two-bone IK applied directly to bone transforms in LateUpdate —
    /// the same bone-based technique PlayerFootGrounder uses for foot planting
    /// (this project deliberately avoids Mecanim's IK-goal pass; see that
    /// class). Solves root→mid→end (e.g. upper arm → forearm → hand) so the
    /// end lands on a target, with a hint position steering the bend (elbow)
    /// direction, and everything blended by weight so it can fade in/out.
    /// </summary>
    public static class TwoBoneIK
    {
        public static void Solve(
            Transform root, Transform mid, Transform end,
            Vector3 targetPosition, Vector3 hintPosition, float weight)
        {
            if (root == null || mid == null || end == null || weight <= 0f)
                return;

            Vector3 rootPos = root.position;
            Vector3 midPos = mid.position;
            Vector3 endPos = end.position;

            float upperLength = Vector3.Distance(rootPos, midPos);
            float lowerLength = Vector3.Distance(midPos, endPos);
            if (upperLength < 1e-4f || lowerLength < 1e-4f)
                return;

            // Clamp the reach so the chain can neither overextend nor fold flat.
            float targetDistance = Mathf.Clamp(
                Vector3.Distance(rootPos, targetPosition), 0.01f, upperLength + lowerLength - 0.001f);

            // 1) Bend the elbow so the root→end distance matches root→target.
            float currentInterior = Vector3.Angle(rootPos - midPos, endPos - midPos);
            float desiredCos = Mathf.Clamp(
                (upperLength * upperLength + lowerLength * lowerLength - targetDistance * targetDistance)
                / (2f * upperLength * lowerLength), -1f, 1f);
            float desiredInterior = Mathf.Acos(desiredCos) * Mathf.Rad2Deg;

            Vector3 bendAxis = Vector3.Cross(rootPos - midPos, endPos - midPos);
            if (bendAxis.sqrMagnitude < 1e-6f)
                bendAxis = Vector3.Cross(rootPos - midPos, hintPosition - midPos);
            bendAxis.Normalize();

            mid.rotation = Quaternion.AngleAxis((desiredInterior - currentInterior) * weight, bendAxis) * mid.rotation;

            // 2) Aim the whole chain so the end lands on the target.
            endPos = end.position;
            Quaternion aim = Quaternion.FromToRotation(endPos - rootPos, targetPosition - rootPos);
            root.rotation = Quaternion.Slerp(Quaternion.identity, aim, weight) * root.rotation;

            // 3) Swing the elbow toward the hint around the root→target axis.
            Vector3 axis = (targetPosition - rootPos).normalized;
            Vector3 toMid = Vector3.ProjectOnPlane(mid.position - rootPos, axis);
            Vector3 toHint = Vector3.ProjectOnPlane(hintPosition - rootPos, axis);
            if (toMid.sqrMagnitude > 1e-6f && toHint.sqrMagnitude > 1e-6f)
            {
                float poleAngle = Vector3.SignedAngle(toMid, toHint, axis);
                root.rotation = Quaternion.AngleAxis(poleAngle * weight, axis) * root.rotation;
            }
        }
    }
}
