using UnityEngine;

namespace RumOverboard.Gameplay.Ocean
{
    /// <summary>
    /// Cosmetic bow-spray + wake foam that scales with how fast the hull moves through the water,
    /// anchored to the ocean waterline. Fully asset-free (see <see cref="FoamParticleFactory"/>).
    ///
    /// Runs on every peer — it derives speed from the (network-synced) transform, so proxies whose
    /// Rigidbody is kinematic still foam correctly. No authority checks needed.
    /// </summary>
    [DisallowMultipleComponent]
    public class ShipWakeFoam : MonoBehaviour
    {
        [SerializeField] private OceanWaveField waveField;

        [Header("Waterline anchors (ship-local)")]
        [Tooltip("Bow point where the hull cuts the water.")]
        [SerializeField] private Vector3 bowLocal = new Vector3(0f, 0f, 4.2f);
        [Tooltip("Stern point the wake trails from.")]
        [SerializeField] private Vector3 sternLocal = new Vector3(0f, 0f, -4.3f);
        [Tooltip("Half the hull width — foam is thrown out to both sides by this much.")]
        [SerializeField] private float halfBeam = 1.9f;
        [SerializeField] private float surfaceOffset = 0.06f;

        [Header("Emission")]
        [Tooltip("Below this speed (m/s) no foam is made.")]
        [SerializeField] private float minSpeed = 0.6f;
        [Tooltip("Speed at which foam output is maxed out.")]
        [SerializeField] private float fullSpeed = 8f;
        [SerializeField] private float bowRate = 130f;   // particles/sec at full speed
        [SerializeField] private float wakeRate = 95f;
        [SerializeField] private float bowPushSpeed = 1.4f;
        [SerializeField] private float sidePushSpeed = 0.8f;

        [Header("Look")]
        [SerializeField] private float bowFoamSize = 1.0f;
        [SerializeField] private float wakeFoamSize = 1.5f;
        [SerializeField] private float bowLifetime = 1.4f;
        [SerializeField] private float wakeLifetime = 2.2f;
        [SerializeField] private Color foamColor = new Color(0.95f, 0.98f, 1f, 0.9f);

        private ParticleSystem _bow;
        private ParticleSystem _wake;
        private Vector3 _prevPos;
        private bool _hasPrev;
        private float _bowAccum;
        private float _wakeAccum;

        private void Awake()
        {
            if (waveField == null)
                waveField = FindAnyObjectByType<OceanWaveField>();

            _bow = FoamParticleFactory.CreateFoamSystem(transform, "WakeFoam_Bow", foamColor, bowFoamSize, bowLifetime);
            _wake = FoamParticleFactory.CreateFoamSystem(transform, "WakeFoam_Trail", foamColor, wakeFoamSize, wakeLifetime);
            _prevPos = transform.position;
        }

        public void ConfigureReferences(OceanWaveField field)
        {
            waveField = field;
        }

        /// <summary>Fit the bow/stern/beam anchors to the actual hull (defaults suit a ~9 m boat).</summary>
        public void ConfigureHull(Vector3 bow, Vector3 stern, float beamHalf, float sizeScale)
        {
            bowLocal = bow;
            sternLocal = stern;
            halfBeam = beamHalf;
            bowFoamSize *= sizeScale;
            wakeFoamSize *= sizeScale;
            bowRate *= sizeScale;
            wakeRate *= sizeScale;
            wakeLifetime *= Mathf.Sqrt(sizeScale);
            if (_bow != null) { var m = _bow.main; m.startSize = bowFoamSize; }
            if (_wake != null) { var m = _wake.main; m.startSize = wakeFoamSize; m.startLifetime = wakeLifetime; m.maxParticles = 6000; }
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            Vector3 pos = transform.position;
            Vector3 vel = _hasPrev ? (pos - _prevPos) / dt : Vector3.zero;
            _prevPos = pos;
            _hasPrev = true;

            vel.y = 0f;
            float speed = vel.magnitude;
            float t = Mathf.Clamp01((speed - minSpeed) / Mathf.Max(0.01f, fullSpeed - minSpeed));
            if (t <= 0f)
                return;

            Vector3 dir = speed > 0.01f ? vel / speed : transform.forward;

            // Bow: foam thrown forward-out as the prow parts the water.
            EmitBand(_bow, bowLocal, ref _bowAccum, bowRate * t, dt, dir * bowPushSpeed * t, sidePushSpeed * (0.5f + t));
            // Wake: foam shed behind, spreading outward and lingering.
            EmitBand(_wake, sternLocal, ref _wakeAccum, wakeRate * t, dt, -dir * 0.35f, sidePushSpeed);
        }

        private void EmitBand(ParticleSystem ps, Vector3 anchorLocal, ref float accum, float rate, float dt, Vector3 pushVel, float sideSpeed)
        {
            if (ps == null)
                return;

            accum += rate * dt;
            int count = Mathf.FloorToInt(accum);
            if (count <= 0)
                return;
            accum -= count;

            Vector3 right = transform.right;
            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };

            for (int i = 0; i < count; i++)
            {
                float sideSign = (i & 1) == 0 ? -1f : 1f;
                float sideFrac = Mathf.Lerp(0.35f, 1f, (i * 0.618f) % 1f); // spread along the beam
                Vector3 local = anchorLocal + new Vector3(sideSign * halfBeam * sideFrac, 0f, 0f);
                Vector3 world = transform.TransformPoint(local);

                float y = waveField != null ? waveField.SampleHeight(world) : world.y;
                world.y = y + surfaceOffset;

                emit.position = world;
                emit.velocity = pushVel + right * (sideSign * sideSpeed);
                ps.Emit(emit, 1);
            }
        }
    }
}
