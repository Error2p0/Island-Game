using IslandGame.Data.Items;
using IslandGame.Inventory;
using IslandGame.Player;
using UnityEngine;

namespace IslandGame.Held
{
    /// <summary>
    /// Makes the equipped hotbar item physically held: subscribes to
    /// HotbarSelector.EquippedItemChanged (Phase 4's equip event), instantiates
    /// the item's world model under the socket named by HoldSocketConvention
    /// (right hand for OneHanded/TwoHanded, left hand for OffHand or items
    /// authored LeftHand) with the Item Editor's authored local offset, and
    /// drives the upper-body Animator layer: HoldPose int selects the pose,
    /// the LAYER WEIGHT blends in/out so empty hands leave the base-layer arm
    /// swing completely untouched, and PlayUse() fires the generic swing.
    ///
    /// TwoHanded items get bone-based off-hand IK in LateUpdate (TwoBoneIK —
    /// same technique as PlayerFootGrounder, never Mecanim IK goals): the left
    /// hand grips the model's 'OffHandGrip' child, or a configurable fallback
    /// point below the item origin when the prefab has none.
    ///
    /// Movement-system respect: the layer weight and IK fade OUT while
    /// Swimming (item also hidden — arms must stroke) and while Prone (arms
    /// crawl); every other stance keeps the hold. Held instances are stripped
    /// of colliders/rigidbodies so they can never shove the CharacterController
    /// or snag terrain. Equipping never changes inventory contents, so carry
    /// weight is untouched by design.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerReferences))]
    public sealed class ItemHoldController : MonoBehaviour
    {
        private static readonly int HoldPoseHash = Animator.StringToHash("HoldPose");
        private static readonly int UseHash = Animator.StringToHash("Use");

        [Header("Sockets (created/wired by Island Game → Player → Create Hold Sockets; auto-created at runtime if missing)")]
        [SerializeField] private Transform rightHandSocket;
        [SerializeField] private Transform leftHandSocket;
        [SerializeField] private Transform twoHandedSocket;

        [Header("Blending")]
        [Tooltip("How fast the upper-body layer weight blends when equipping/unequipping (per second).")]
        [SerializeField] private float layerBlendSpeed = 6f;

        [Tooltip("How fast the off-hand IK fades in/out (per second).")]
        [SerializeField] private float ikBlendSpeed = 8f;

        [Header("Off-Hand Grip (two-handed items)")]
        [Tooltip("Fallback grip point in the ITEM's local space when the world model has no 'OffHandGrip' child.")]
        [SerializeField] private Vector3 fallbackGripLocalOffset = new Vector3(0f, -0.28f, 0f);

        [Tooltip("Elbow hint offset in PLAYER space, from the left shoulder — keeps the elbow bending down-and-out, never inverted.")]
        [SerializeField] private Vector3 elbowHintLocalOffset = new Vector3(-0.45f, -0.35f, -0.15f);

        /// <summary>The definition currently held (null = empty hands / non-holdable equipped).</summary>
        public ItemDefinition HeldItem { get; private set; }

        /// <summary>The instantiated world model, for later phases (weapon trails, torch light). Null when nothing is held.</summary>
        public GameObject HeldInstance { get; private set; }

        private PlayerReferences references;
        private Animator animator;
        private HotbarSelector selector;

        private int upperBodyLayer = -1;
        private Transform offHandGrip;
        private Transform leftUpperArm;
        private Transform leftLowerArm;
        private Transform leftHand;
        private float layerWeight;
        private float ikWeight;

        private void Awake()
        {
            references = GetComponent<PlayerReferences>();
            animator = GetComponent<Animator>();
            selector = GetComponent<HotbarSelector>();
        }

        private void Start()
        {
            upperBodyLayer = animator.GetLayerIndex("UpperBody");
            if (upperBodyLayer < 0)
                Debug.LogError("ItemHoldController: Animator has no 'UpperBody' layer — rerun Tools/Island Game/Build Player Animations & Controller.", this);

            leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            leftLowerArm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);

            ResolveSockets();

            if (selector != null)
                Equip(selector.EquippedItem);
        }

        private void OnEnable()
        {
            if (selector != null)
                selector.EquippedItemChanged += Equip;
        }

        private void OnDisable()
        {
            if (selector != null)
                selector.EquippedItemChanged -= Equip;
        }

        private void Update()
        {
            if (upperBodyLayer < 0)
                return;

            PlayerState state = references.StateMachine.CurrentState;

            // Swimming: arms stroke and the item holsters. Prone: arms crawl.
            bool suppressed = state == PlayerState.Swimming || state == PlayerState.Prone;
            float targetWeight = HeldItem != null && !suppressed ? 1f : 0f;
            layerWeight = Mathf.MoveTowards(layerWeight, targetWeight, layerBlendSpeed * Time.deltaTime);
            animator.SetLayerWeight(upperBodyLayer, layerWeight);

            if (HeldInstance != null)
            {
                bool visible = state != PlayerState.Swimming;
                if (HeldInstance.activeSelf != visible)
                    HeldInstance.SetActive(visible);
            }
        }

        private void LateUpdate()
        {
            // Off-hand IK, bone-based after the Animator wrote the pose.
            bool wantIK = HeldItem != null
                          && HeldItem.HoldType == HoldType.TwoHanded
                          && HeldInstance != null
                          && HeldInstance.activeSelf
                          && layerWeight > 0.01f;

            ikWeight = Mathf.MoveTowards(ikWeight, wantIK ? layerWeight : 0f, ikBlendSpeed * Time.deltaTime);
            if (ikWeight <= 0.001f || leftUpperArm == null || leftLowerArm == null || leftHand == null || HeldInstance == null)
                return;

            Vector3 gripPosition = offHandGrip != null
                ? offHandGrip.position
                : HeldInstance.transform.TransformPoint(fallbackGripLocalOffset);
            Quaternion gripRotation = offHandGrip != null ? offHandGrip.rotation : HeldInstance.transform.rotation;
            Vector3 hint = leftUpperArm.position + transform.TransformVector(elbowHintLocalOffset);

            TwoBoneIK.Solve(leftUpperArm, leftLowerArm, leftHand, gripPosition, hint, ikWeight);
            leftHand.rotation = Quaternion.Slerp(leftHand.rotation, gripRotation, ikWeight);
        }

        /// <summary>Fires the generic Use swing on the upper-body layer (mining loop, weapon attacks).</summary>
        public void PlayUse()
        {
            if (upperBodyLayer >= 0 && HeldItem != null && layerWeight > 0.1f)
                animator.SetTrigger(UseHash);
        }

        // ------------------------------------------------------------------
        // Equip / unequip
        // ------------------------------------------------------------------

        private void Equip(ItemDefinition item)
        {
            if (HeldInstance != null)
            {
                Destroy(HeldInstance);
                HeldInstance = null;
            }

            offHandGrip = null;
            HeldItem = item != null && item.HoldType != HoldType.None ? item : null;

            if (HeldItem != null && HeldItem.WorldModelPrefab != null)
            {
                Transform socket = HeldItem.HoldSocket == HoldSocket.LeftHand || HeldItem.HoldType == HoldType.OffHand
                    ? leftHandSocket
                    : rightHandSocket;

                if (socket != null)
                {
                    HeldInstance = Instantiate(HeldItem.WorldModelPrefab, socket);
                    HeldInstance.transform.localPosition = HeldItem.HoldLocalPosition;
                    HeldInstance.transform.localRotation = HeldItem.HoldLocalRotation;
                    HeldInstance.transform.localScale = Vector3.one;

                    StripPhysics(HeldInstance);
                    offHandGrip = FindDeep(HeldInstance.transform, HoldSocketConvention.OffHandGripName);
                }
            }

            int pose = HeldItem == null ? 0 : HeldItem.HoldType == HoldType.TwoHanded ? 2 : 1;
            animator.SetInteger(HoldPoseHash, pose);
        }

        /// <summary>Held models must never collide or simulate — disable immediately, then remove.</summary>
        private static void StripPhysics(GameObject instance)
        {
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
                Destroy(collider);
            }

            foreach (Rigidbody body in instance.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
                Destroy(body);
            }
        }

        // ------------------------------------------------------------------
        // Sockets
        // ------------------------------------------------------------------

        private void ResolveSockets()
        {
            Transform rightHandBone = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform chestBone = animator.GetBoneTransform(HumanBodyBones.Chest);
            if (chestBone == null)
                chestBone = animator.GetBoneTransform(HumanBodyBones.Spine);

            // Editor-built sockets are preferred (offsets tunable in the scene);
            // missing ones are created at runtime with the same defaults so the
            // system degrades gracefully instead of silently holding nothing.
            if (rightHandSocket == null)
                rightHandSocket = FindOrCreateSocket(rightHandBone, HoldSocketConvention.RightHandSocketName, new Vector3(0.08f, 0f, 0f));
            if (leftHandSocket == null)
                leftHandSocket = FindOrCreateSocket(leftHand, HoldSocketConvention.LeftHandSocketName, new Vector3(-0.08f, 0f, 0f));
            if (twoHandedSocket == null)
                twoHandedSocket = FindOrCreateSocket(chestBone, HoldSocketConvention.TwoHandedSocketName, new Vector3(0f, 0.1f, 0.35f));
        }

        private static Transform FindOrCreateSocket(Transform bone, string socketName, Vector3 localPosition)
        {
            if (bone == null)
                return null;

            Transform existing = bone.Find(socketName);
            if (existing != null)
                return existing;

            var socket = new GameObject(socketName).transform;
            socket.SetParent(bone, false);
            socket.localPosition = localPosition;
            socket.localRotation = Quaternion.identity;
            return socket;
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
