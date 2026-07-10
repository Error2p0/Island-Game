using UnityEngine;

namespace IslandGame.Player
{
    /// <summary>
    /// First-person mouse/stick look. Yaw rotates the body root (so movement and
    /// aim share a facing); pitch is applied to the camera pivot in WORLD terms
    /// (root yaw * pitch), so stance poses or future animations rotating the
    /// head bone move the camera's position but can never tilt the view.
    /// While prone, yaw speed is rate-limited and pitch range narrows so the
    /// character can't spin or stare through the ground while flat.
    /// Runs in LateUpdate so it wins over anything applied to bones in Update.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FirstPersonCameraController : MonoBehaviour
    {
        [Header("Sensitivity")]
        [Tooltip("Degrees per pixel of mouse delta.")]
        [SerializeField] private float mouseSensitivity = 0.1f;

        [Tooltip("Degrees per second at full gamepad stick deflection.")]
        [SerializeField] private float stickSensitivity = 150f;

        [SerializeField] private bool invertY;

        [Header("Pitch Clamp — Standing")]
        [SerializeField] private float minPitch = -85f;
        [SerializeField] private float maxPitch = 85f;

        [Header("Prone Look Restrictions")]
        [Tooltip("Maximum turn rate while fully prone, degrees/second. Standing turn rate is unlimited.")]
        [SerializeField] private float proneYawSpeedLimit = 90f;

        [Tooltip("Pitch range at full prone (min = up, max = down). Narrower than standing: no spinning the head into the dirt.")]
        [SerializeField] private float proneMinPitch = -45f;
        [SerializeField] private float proneMaxPitch = 30f;

        /// <summary>Current camera pitch in degrees (negative = looking up).</summary>
        public float Pitch { get; private set; }

        private PlayerReferences references;

        private float ProneBlend =>
            references.Prone != null ? references.Prone.ProneBlend01 : 0f;

        private void Awake()
        {
            references = GetComponent<PlayerReferences>();
        }

        private void Start()
        {
            SetCursorLocked(true);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
                SetCursorLocked(true);
        }

        private void LateUpdate()
        {
            Vector2 look = references.InputHandler.LookInput;

            // Mouse deltas are per-frame values; only stick input is a rate that
            // needs deltaTime scaling (see PlayerInputHandler.LookInputIsPointerDelta).
            float scale = references.InputHandler.LookInputIsPointerDelta
                ? mouseSensitivity
                : stickSensitivity * Time.deltaTime;

            float yawDelta = look.x * scale;
            float pitchDelta = look.y * scale * (invertY ? -1f : 1f);

            float proneBlend = ProneBlend;
            if (proneBlend > 0f)
            {
                // Rate-limit yaw while prone; blend the limit in with the stance
                // so dropping down doesn't snap the turn feel.
                float maxYawStep = Mathf.Lerp(3600f, proneYawSpeedLimit, proneBlend) * Time.deltaTime;
                yawDelta = Mathf.Clamp(yawDelta, -maxYawStep, maxYawStep);
            }

            transform.Rotate(0f, yawDelta, 0f, Space.Self);

            float pitchMin = Mathf.Lerp(minPitch, proneMinPitch, proneBlend);
            float pitchMax = Mathf.Lerp(maxPitch, proneMaxPitch, proneBlend);
            Pitch = Mathf.Clamp(Pitch - pitchDelta, pitchMin, pitchMax);

            // World-space pitch: root yaw * pitch. Immune to head-bone rotation
            // from stance poses (prone incline) or future animations.
            references.CameraPivot.rotation = transform.rotation * Quaternion.Euler(Pitch, 0f, 0f);
        }

        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
