#if FUSION2
using System.Collections.Generic;
using Fusion;
using RumOverboard.Gameplay.Attributes;
using RumOverboard.Gameplay.Ocean;
using UnityEngine;

namespace RumOverboard.Gameplay.Items
{
    /// <summary>
    /// A physical, networked object of the world (barrel, board, bottle, spyglass, chest…) — GDD §3.4:
    /// there are only actual objects. Host authority like everything else: the host simulates the
    /// body (NetworkRigidbody3D replicates it), clients only render.
    ///
    /// Everything about the type is in its attribute profile:
    ///   Mass → Rigidbody.mass · Floating flag → buoyancy · Carriable flag → can be picked up ·
    ///   Integrity + ImpactDamageThreshold/Scale → breaks from hits (and anything else that drains
    ///   Integrity: fire, the debug window…) · BreakableData → debris / sound.
    ///
    /// Networked state: <see cref="Holder"/> (crew member carrying it), <see cref="Container"/>
    /// (storage it sits in — hidden, no collisions), <see cref="Broken"/> (every peer shows the
    /// debris, the host despawns it shortly after).
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(AttributesComponent), typeof(NetworkAttributes))]
    public sealed class WorldItem : NetworkBehaviour
    {
        public static readonly List<WorldItem> All = new();

        private const float BreakDespawnDelay = 0.5f;
        private const float SpawnGrace = 1f;          // s without impact damage after spawning (settling)
        private const float FloatBuoyancy = 1.7f;   // × weight at full submersion → floats ~60% under
        private const float WaterLinearDrag = 1.5f;
        private const float WaterAngularDrag = 1.0f;

        [Networked] public PlayerRef Holder { get; set; }
        [Networked] public NetworkId Container { get; set; }
        [Networked] public NetworkBool Broken { get; set; }
        [Networked] private int BrokenTick { get; set; }

        private Rigidbody _rb;
        private AttributesComponent _attributes;
        private Collider[] _colliders;
        private Renderer[] _renderers;
        private OceanWaveField _ocean;
        private int _massVersion = -1;
        private bool _kinematicByStorage;
        private bool _hidden;
        private bool _showedBreak;
        private Vector3 _preStepVelocity;
        private int _spawnTick;
        private Fusion.Addons.Physics.NetworkRigidbody3D _nrb;
        private bool _pendingImpact;
        private Vector3 _pendingOtherVelocity;
        private Vector3 _impactStartVelocity;
        private int _impactTicks;
        private const int ImpactWindowTicks = 2;
        private const int VelocityHistory = 3;     // ticks of pre-step velocity kept for impact measurement
        private readonly Vector3[] _velocityHistory = new Vector3[VelocityHistory];
        private int _historyIndex;
        private int _impactExtraTicks;

        public Rigidbody Body => _rb;

        /// <summary>Host debug: normal impact speed (m/s) and what was hit, of the last collision.</summary>
        public float LastImpactSpeed { get; private set; }
        public string LastImpactWith { get; private set; } = "";
        public float LastImpactPreStep { get; private set; }
        public AttributesComponent Attributes => _attributes;
        public IReadOnlyList<Collider> Colliders => _colliders;

        public bool IsHeld => Holder != PlayerRef.None;
        public bool IsStored => Container.IsValid;
        public bool IsCarriable => !Broken && _attributes != null && _attributes.HasFlag(AttrFlag.Carriable);
        public float Mass => _attributes != null ? _attributes.Get(Attr.Mass, _rb.mass) : _rb.mass;

        public string DisplayName
        {
            get
            {
                DisplayData d = _attributes != null ? _attributes.GetData<DisplayData>() : null;
                return d != null && !string.IsNullOrEmpty(d.DisplayName) ? d.DisplayName : name.Replace("(Clone)", "");
            }
        }

        /// <summary>World bounds of all colliders (for hold distance, buoyancy, debris size).</summary>
        public Bounds WorldBounds
        {
            get
            {
                Bounds b = new Bounds(transform.position, Vector3.zero);
                bool any = false;
                foreach (Collider c in _colliders)
                {
                    if (c == null) continue;
                    if (!any) { b = c.bounds; any = true; }
                    else b.Encapsulate(c.bounds);
                }
                return b;
            }
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _attributes = GetComponent<AttributesComponent>();
            _colliders = GetComponentsInChildren<Collider>(true);
            _renderers = GetComponentsInChildren<Renderer>(true);
            _nrb = GetComponent<Fusion.Addons.Physics.NetworkRigidbody3D>();
        }

        public override void Spawned()
        {
            All.Add(this);
            _ocean = FindAnyObjectByType<OceanWaveField>();
            _spawnTick = Runner.Tick;
            ApplyMassIfChanged();
            UpdateVisibility();
        }

        public override void Despawned(NetworkRunner runner, bool hasState) => All.Remove(this);

        // ───────────────────────── host simulation ─────────────────────────

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            ApplyMassIfChanged();
            ResolvePendingImpact();

            if (Broken)
            {
                if ((Runner.Tick - BrokenTick) * Runner.DeltaTime >= BreakDespawnDelay)
                    Runner.Despawn(Object);
                return;
            }

            AttributeSet set = _attributes.Set;
            if (set != null && set.Has(Attr.Integrity) && set.GetCurrent(Attr.Integrity) <= 0f)
            {
                Break();
                return;
            }

            if (IsStored)
            {
                FollowContainer();
                return;
            }

            if (_kinematicByStorage)
            {
                _rb.isKinematic = false;
                _kinematicByStorage = false;
            }

            if (!IsHeld)
                ApplyWater(Runner.DeltaTime);

            // Velocity going into this physics step: speculative CCD removes most of the approach speed
            // before OnCollisionEnter, so collision.relativeVelocity alone under-reports hard hits.
            _preStepVelocity = _rb.linearVelocity;
            _velocityHistory[_historyIndex = (_historyIndex + 1) % VelocityHistory] = _preStepVelocity;
        }

        /// <summary>
        /// Host: move the body in one jump. Goes through NetworkRigidbody3D.Teleport so the jump is part of the
        /// replicated state (no interpolated streak on clients) and a write made between ticks isn't rolled back.
        /// </summary>
        public void Teleport(Vector3 position, Quaternion? rotation = null)
        {
            if (_nrb != null && Object != null && Object.IsValid) _nrb.Teleport(position, rotation);
            System.Array.Clear(_velocityHistory, 0, VelocityHistory); // speeds from before the jump aren't an approach
            _rb.position = position;
            if (rotation.HasValue) _rb.rotation = rotation.Value;
        }

        /// <summary>Host: break now — spill contents, every peer shows the debris, despawn shortly.</summary>
        public void Break()
        {
            if (!HasStateAuthority || Broken) return;
            if (TryGetComponent(out ItemStorage storage))
                storage.SpillAll();
            Holder = PlayerRef.None;
            Broken = true;
            BrokenTick = Runner.Tick;
        }

        /// <summary>Host: park inside a container (hidden, no collisions) or take back out (default id).</summary>
        public void SetContainer(NetworkId container)
        {
            if (!HasStateAuthority) return;
            Container = container;
            if (container.IsValid)
            {
                Holder = PlayerRef.None;
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                _rb.isKinematic = true;
                _kinematicByStorage = true;
                FollowContainer();
            }
            UpdateVisibility();
        }

        private void FollowContainer()
        {
            if (!Runner.TryFindObject(Container, out NetworkObject owner) || owner == null)
                return;
            StorageData data = owner.TryGetComponent(out AttributesComponent a) ? a.GetData<StorageData>() : null;
            Vector3 local = data != null ? data.StowOffset : Vector3.zero;
            Teleport(owner.transform.TransformPoint(local), owner.transform.rotation);
        }

        private void ApplyMassIfChanged()
        {
            AttributeSet set = _attributes != null ? _attributes.Set : null;
            if (set == null || set.Version == _massVersion) return;
            _massVersion = set.Version;
            if (set.Has(Attr.Mass))
                _rb.mass = Mathf.Max(0.1f, set.Get(Attr.Mass));
        }

        private void ApplyWater(float dt)
        {
            if (_ocean == null || _rb.isKinematic) return;
            Bounds b = WorldBounds;
            float surface = _ocean.SampleHeight(b.center);
            float submerged = ItemRules.SubmergedFraction(surface, b.min.y, b.size.y);
            if (submerged <= 0f) return;

            if (_attributes.HasFlag(AttrFlag.Floating))
                _rb.AddForce(Vector3.up * (-Physics.gravity.y * FloatBuoyancy * submerged), ForceMode.Acceleration);
            _rb.linearVelocity *= Mathf.Clamp01(1f - WaterLinearDrag * submerged * dt);
            _rb.angularVelocity *= Mathf.Clamp01(1f - WaterAngularDrag * submerged * dt);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (Object == null || !Object.IsValid || !HasStateAuthority || Broken || IsHeld || IsStored)
                return;
            if ((Runner.Tick - _spawnTick) * Runner.DeltaTime < SpawnGrace) // settling right after spawn
                return;
            AttributeSet set = _attributes.Set;
            if (set == null || !set.Has(Attr.Integrity) || collision.contactCount == 0)
                return;
            // Only mark the contact here: with speculative CCD the hit is already absorbed before this event
            // (impulse and relativeVelocity read ~0). The damage is measured next host tick from the actual
            // velocity change of the contact step — see ResolvePendingImpact.
            Vector3 otherVelocity = collision.rigidbody != null ? collision.rigidbody.GetPointVelocity(collision.GetContact(0).point) : Vector3.zero;
            if (!_pendingImpact || (_preStepVelocity - otherVelocity).sqrMagnitude > (_preStepVelocity - _pendingOtherVelocity).sqrMagnitude)
            {
                _pendingOtherVelocity = otherVelocity;
                LastImpactWith = collision.collider != null ? collision.collider.name : "?";
            }
            if (!_pendingImpact)
            {
                // Velocity going into the contact step — or a tick or two earlier: a speculative contact can stop
                // the body just short of the surface, and the touch (this event) comes a step later at ~0 speed.
                _impactStartVelocity = _preStepVelocity;
                _impactExtraTicks = 0;
                for (int k = 1; k < VelocityHistory; k++)
                {
                    Vector3 v = _velocityHistory[(_historyIndex - k + VelocityHistory) % VelocityHistory];
                    if (v.sqrMagnitude > _impactStartVelocity.sqrMagnitude) { _impactStartVelocity = v; _impactExtraTicks = k; }
                }
                _impactTicks = 0;
            }
            _pendingImpact = true;
        }

        /// <summary>
        /// Host, start of the tick after a contact began: impact = velocity change of that physics step
        /// (gravity removed), capped by the approach speed going into it — so depenetration pushes (no
        /// approach) aren't hits, and an edge being the first contact doesn't hide a hard landing.
        /// </summary>
        private void ResolvePendingImpact()
        {
            if (!_pendingImpact) return;
            // A hit is absorbed over the speculative-contact step and the next one: measure across both.
            if (++_impactTicks < ImpactWindowTicks) return;
            _pendingImpact = false;
            AttributeSet set = _attributes.Set;
            if (Broken || IsHeld || IsStored || set == null || !set.Has(Attr.Integrity)) return;

            Vector3 deltaV = _rb.linearVelocity - _impactStartVelocity - Physics.gravity * (Runner.DeltaTime * (_impactTicks + _impactExtraTicks));
            float approach = (_impactStartVelocity - _pendingOtherVelocity).magnitude;
            float impact = ItemRules.ImpactSpeed(deltaV.magnitude, approach);
            LastImpactSpeed = impact;
            LastImpactPreStep = _impactStartVelocity.magnitude;

            float damage = ItemRules.ImpactDamage(impact, set.Get(Attr.ImpactDamageThreshold, float.MaxValue),
                                                  set.Get(Attr.ImpactDamageScale));
            if (damage > 0f)
                set.AddCurrent(Attr.Integrity, -damage);
        }

        // ───────────────────────── presentation (every peer) ─────────────────────────

        public override void Render()
        {
            UpdateVisibility();
            if (Broken && !_showedBreak)
            {
                _showedBreak = true;
                ShowBreak();
            }
        }

        private void UpdateVisibility()
        {
            bool hide = IsStored || Broken;
            if (hide == _hidden) return;
            _hidden = hide;
            foreach (Renderer r in _renderers) if (r != null) r.enabled = !hide;
            foreach (Collider c in _colliders) if (c != null) c.enabled = !hide;
        }

        private void ShowBreak()
        {
            BreakableData data = _attributes != null ? _attributes.GetData<BreakableData>() : null;
            Bounds b = WorldBounds;
            if (data != null && data.DebrisPrefab != null)
                Instantiate(data.DebrisPrefab, transform.position, transform.rotation);
            else
                DebrisChunk.Burst(b, _renderers.Length > 0 && _renderers[0] != null ? _renderers[0].sharedMaterial : null);
            if (data != null && data.BreakSound != null)
                AudioSource.PlayClipAtPoint(data.BreakSound, b.center);
        }
    }

}
#endif
