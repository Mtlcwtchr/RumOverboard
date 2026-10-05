#if FUSION2
using RumOverboard.Core.Character;
using UnityEngine;

namespace RumOverboard.Gameplay.Interaction
{
    /// <summary>
    /// Authored climbable area: a straight "rail" from <see cref="bottomLocal"/> to
    /// <see cref="topLocal"/> (this transform's space) with a lateral half-width, climbed from the
    /// <see cref="outwardLocal"/> side. Covers ladders, mast trunks and shroud nets — the climber
    /// is parametrised by (u along the rail, v sideways) and the Climbing state drives the body to
    /// that point through <see cref="RumOverboard.Core.Character.AttachMotor"/>, so it rides the
    /// mast/ship motion exactly.
    ///
    /// Exits: at the top, if <see cref="topExit"/> is set (crow's nest, yard footrope platform),
    /// climbing further steps the crew member off onto it; at the bottom they simply let go.
    /// </summary>
    public sealed class ClimbSurface : Interactable, IClimbRail
    {
        [Header("Rail (local space)")]
        [SerializeField] private Vector3 bottomLocal = Vector3.zero;
        [SerializeField] private Vector3 topLocal = new Vector3(0f, 6f, 0f);

        [Tooltip("Direction (local) pointing from the surface toward where the climber hangs.")]
        [SerializeField] private Vector3 outwardLocal = Vector3.back;

        [Tooltip("Half-width the climber can shimmy sideways (m).")]
        [SerializeField] private float halfWidth = 0.3f;

        [Tooltip("Distance from the rail to the climber's body centre line (capsule radius + margin).")]
        [SerializeField] private float standOff = 0.42f;

        [Tooltip("Distance from the rail line to the physical surface on the climber's side (mast radius; 0 for a wall).")]
        [SerializeField] private float surfaceOffset = 0.3f;

        [Tooltip("Vertical offset from the rail point down to the climber's feet (pivot).")]
        [SerializeField] private float feetBelowGrip = 1.15f;

        [Header("Exits")]
        [Tooltip("Optional: where the climber steps off at the top (platform / crow's nest).")]
        [SerializeField] private Transform topExit;

        [Header("Tuning")]
        [SerializeField] private float speedMultiplier = 1f;

        private Rigidbody _body;

        public override InteractionKind Kind => InteractionKind.Climb;

        public float Length => (topLocal - bottomLocal).magnitude;
        public float HalfWidth => halfWidth;
        public Transform TopExit => topExit;
        public float SpeedMultiplier => speedMultiplier;

        /// <summary>The rigidbody the surface moves with (mast or hull) — for point velocity.</summary>
        public Rigidbody Body
        {
            get
            {
                if (_body == null)
                    _body = GetComponentInParent<Rigidbody>();
                return _body;
            }
        }

        public void Configure(Vector3 bottom, Vector3 top, Vector3 outward, float width, float offset, Transform exit,
            float surface)
        {
            surfaceOffset = surface;
            bottomLocal = bottom;
            topLocal = top;
            outwardLocal = outward;
            halfWidth = width;
            standOff = offset;
            topExit = exit;
        }

        public Vector3 AxisWorld => transform.TransformDirection(topLocal - bottomLocal).normalized;

        public Vector3 OutwardWorld
        {
            get
            {
                Vector3 axis = AxisWorld;
                Vector3 o = Vector3.ProjectOnPlane(transform.TransformDirection(outwardLocal), axis);
                return o.sqrMagnitude > 1e-6f ? o.normalized : -transform.forward;
            }
        }

        public Vector3 LateralWorld => Vector3.Cross(AxisWorld, OutwardWorld).normalized;

        /// <summary>World grip point for rail coordinates (u along, v sideways), on the rail itself.</summary>
        public Vector3 GripPoint(float u, float v)
        {
            Vector3 bottom = transform.TransformPoint(bottomLocal);
            return bottom + AxisWorld * Mathf.Clamp(u, 0f, Length) + LateralWorld * Mathf.Clamp(v, -halfWidth, halfWidth);
        }

        /// <summary>Point ON the climbed surface (where hands and feet go) for rail coordinates.</summary>
        public Vector3 SurfacePoint(float u, float v) => GripPoint(u, v) + OutwardWorld * surfaceOffset;

        /// <summary>Rail coordinates of a climber whose root (feet pivot) is at <paramref name="bodyRoot"/>.</summary>
        public void ProjectBody(Vector3 bodyRoot, out float u, out float v) =>
            Project(bodyRoot + Vector3.up * feetBelowGrip - OutwardWorld * standOff, out u, out v);

        /// <summary>Where the climber's root (feet pivot) goes for (u, v).</summary>
        public Vector3 BodyPoint(float u, float v)
        {
            return GripPoint(u, v) + OutwardWorld * standOff + Vector3.down * feetBelowGrip;
        }

        /// <summary>Body yaw-only facing: looking at the surface.</summary>
        public Quaternion BodyRotation
        {
            get
            {
                Vector3 face = -OutwardWorld;
                face.y = 0f;
                if (face.sqrMagnitude < 1e-4f)
                    face = Vector3.ProjectOnPlane(-transform.forward, Vector3.up);
                return Quaternion.LookRotation(face.normalized, Vector3.up);
            }
        }

        /// <summary>Rail coordinates closest to a world point (used when grabbing on).</summary>
        public void Project(Vector3 worldGrip, out float u, out float v)
        {
            Vector3 bottom = transform.TransformPoint(bottomLocal);
            Vector3 d = worldGrip - bottom;
            u = Mathf.Clamp(Vector3.Dot(d, AxisWorld), 0f, Length);
            v = Mathf.Clamp(Vector3.Dot(d, LateralWorld), -halfWidth, halfWidth);
        }

        public override bool IsAvailable(in InteractorInfo who) => Length > 0.1f && who.HandsFree;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 1f, 0.9f, 0.9f);
            Vector3 b = transform.TransformPoint(bottomLocal);
            Vector3 t = transform.TransformPoint(topLocal);
            Vector3 lat = (Application.isPlaying ? LateralWorld : Vector3.Cross(t - b, transform.TransformDirection(outwardLocal)).normalized) * halfWidth;
            Gizmos.DrawLine(b - lat, t - lat);
            Gizmos.DrawLine(b + lat, t + lat);
            Gizmos.DrawLine(b - lat, b + lat);
            Gizmos.DrawLine(t - lat, t + lat);
            Gizmos.color = new Color(1f, 0.7f, 0.2f, 0.9f);
            Vector3 mid = (b + t) * 0.5f;
            Gizmos.DrawLine(mid, mid + transform.TransformDirection(outwardLocal).normalized * standOff);
            if (topExit != null)
            {
                Gizmos.DrawWireSphere(topExit.position, 0.2f);
                Gizmos.DrawLine(t, topExit.position);
            }
        }
    }
}
#endif
