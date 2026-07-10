using System;
using System.Collections.Generic;
using UnityEngine;

namespace IslandGame.Data.Building
{
    /// <summary>
    /// SNAP SOCKET CONVENTION — Phase 2's placement system depends on every
    /// rule in this comment; change them here and in DataConventions.md
    /// together or not at all.
    ///
    /// A socket is a named local-space attachment frame on a building piece:
    /// position + rotation in the PREFAB ROOT's local space.
    ///
    /// ORIENTATION: a socket's +Z (forward) points OUT of the piece — the
    /// direction from which a neighbor arrives. +Y is the socket's up and is
    /// +Y of the piece for every horizontal joint (edges of foundations,
    /// walls, floors, roofs). Vertical stacking joints (wall-on-foundation,
    /// wall-on-wall) also keep horizontal forwards: the foundation's top-edge
    /// socket points outward over the edge, the wall's bottom socket points
    /// backward out of the wall's face (local -Z, i.e. rotation Y=180) — see
    /// the mating math below for why this makes a wall stand upright on an
    /// edge with its face aligned to the edge's outward direction.
    ///
    /// MATING RULE (the Phase 2 contract, implemented by SolveMatedPose): a
    /// ghost piece snaps socket G onto placed socket T so that
    ///   • world position of G == world position of T, and
    ///   • world rotation of G == world rotation of T rotated 180° about T's
    ///     local up (+Y) — forwards end up opposite, ups stay aligned.
    /// With unrotated example pieces this yields: foundations tile edge to
    /// edge, a wall stands centered on a foundation's top edge, walls stack,
    /// floors tile flush with foundation tops. The rule fixes position
    /// exactly; when a piece could face two ways (roof ascending inward vs
    /// outward) the placement UI's rotate key flips it — sockets don't encode
    /// that choice.
    ///
    /// TAGGING: Tag says what this socket IS ("foundation_top"); AcceptedTags
    /// says what may snap here ("wall_bottom", "floor_edge"). Two sockets can
    /// mate when EITHER side accepts the other's tag (CanMate) — so authoring
    /// the link on one side is sufficient, and authoring it on both (like the
    /// example content does) is harmless self-documentation. Tags are stable
    /// serialized strings, lowercase_underscore like every ID in the project;
    /// the standard set lives in SnapTags, custom tags are legal.
    /// </summary>
    [Serializable]
    public sealed class SnapSocket
    {
        [Tooltip("Designer-facing name shown in the editor and gizmos (e.g. 'edge_north'). Not used for matching — tags are.")]
        [SerializeField] private string socketName;

        [Tooltip("Attachment point in the prefab root's local space.")]
        [SerializeField] private Vector3 localPosition;

        [Tooltip("Socket frame rotation (euler, local). +Z must point OUT of the piece, +Y up — see the class summary.")]
        [SerializeField] private Vector3 localRotationEuler;

        [Tooltip("What this socket IS, lowercase_underscore (e.g. 'foundation_top'). Standard tags live in SnapTags.")]
        [SerializeField] private string tag;

        [Tooltip("Tags of sockets allowed to snap onto this one (e.g. a foundation top accepts 'wall_bottom'). Matching is either-direction — see SnapSocket.CanMate.")]
        [SerializeField] private List<string> acceptedTags = new List<string>();

        public SnapSocket()
        {
        }

        public SnapSocket(string socketName, Vector3 localPosition, Vector3 localRotationEuler, string tag, params string[] acceptedTags)
        {
            this.socketName = socketName;
            this.localPosition = localPosition;
            this.localRotationEuler = localRotationEuler;
            this.tag = tag;
            this.acceptedTags = new List<string>(acceptedTags);
        }

        public string SocketName => socketName;
        public Vector3 LocalPosition => localPosition;
        public Vector3 LocalRotationEuler => localRotationEuler;
        public Quaternion LocalRotation => Quaternion.Euler(localRotationEuler);
        public string Tag => tag;
        public IReadOnlyList<string> AcceptedTags => acceptedTags;

        /// <summary>True when this socket's accepted list names the given tag.</summary>
        public bool Accepts(string otherTag)
        {
            if (string.IsNullOrEmpty(otherTag))
                return false;

            for (int i = 0; i < acceptedTags.Count; i++)
            {
                if (string.Equals(acceptedTags[i], otherTag, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The tagging contract: two sockets may mate when either side accepts
        /// the other's tag. One-sided authoring is sufficient by design.
        /// </summary>
        public static bool CanMate(SnapSocket a, SnapSocket b)
        {
            if (a == null || b == null)
                return false;

            return a.Accepts(b.Tag) || b.Accepts(a.Tag);
        }

        /// <summary>
        /// The mating math (see class summary): computes the ghost piece's
        /// ROOT pose that puts this socket (on the ghost) exactly onto the
        /// target socket's world frame, forwards opposed, ups aligned. Pure
        /// math — Phase 2 placement calls this, and defining it here keeps
        /// the convention in code instead of prose.
        /// </summary>
        public void SolveMatedPose(
            Vector3 targetSocketWorldPosition, Quaternion targetSocketWorldRotation,
            out Vector3 ghostRootPosition, out Quaternion ghostRootRotation)
        {
            Quaternion matedSocketWorldRotation =
                targetSocketWorldRotation * Quaternion.AngleAxis(180f, Vector3.up);

            ghostRootRotation = matedSocketWorldRotation * Quaternion.Inverse(LocalRotation);
            ghostRootPosition = targetSocketWorldPosition - ghostRootRotation * localPosition;
        }
    }

    /// <summary>
    /// The standard snap tags. These are serialized strings, not an enum, so
    /// new content can introduce tags without touching code — but shared
    /// vocabulary belongs here so pieces authored by different people mate.
    /// Never rename a shipped tag (assets store the string).
    /// </summary>
    public static class SnapTags
    {
        public const string FoundationTop = "foundation_top";   // top edge of a foundation — walls and floors sit here
        public const string FoundationSide = "foundation_side"; // vertical side of a foundation — foundations tile against it
        public const string WallBottom = "wall_bottom";         // bottom edge of a wall/door/window piece
        public const string WallTop = "wall_top";               // top edge of a wall — upper walls, floors and roofs sit here
        public const string WallSide = "wall_side";             // vertical side edge of a wall — walls continue the line
        public const string FloorEdge = "floor_edge";           // edge of a floor tile
        public const string RoofBottom = "roof_bottom";         // lower (eave) edge of a roof piece
        public const string RoofTop = "roof_top";               // upper (ridge-side) edge of a roof piece — next roof row continues
        public const string RoofSide = "roof_side";             // lateral edge of a roof piece — roof row extends sideways
        public const string Doorway = "doorway";                // hinge mount inside a door frame
        public const string DoorHinge = "door_hinge";           // hinge edge of a door leaf

        /// <summary>Editor dropdowns enumerate this — append new standard tags here.</summary>
        public static readonly string[] All =
        {
            FoundationTop, FoundationSide,
            WallBottom, WallTop, WallSide,
            FloorEdge,
            RoofBottom, RoofTop, RoofSide,
            Doorway, DoorHinge,
        };
    }
}
