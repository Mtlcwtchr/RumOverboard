#if FUSION2
using System.Collections.Generic;
using Fusion;
using RumOverboard.Gameplay.Attributes;
using RumOverboard.Gameplay.Items;
using RumOverboard.Gameplay.Ocean;
using RumOverboard.Networking;
using UnityEngine;

namespace RumOverboard.Gameplay.Fire
{
    /// <summary>
    /// Host-only fire simulation (clients just render the replicated Burning / Exploded flags — FireVisuals).
    /// Registers itself on the host runner (AddGlobal) and runs in its tick:
    ///   • burning items (OnFireFx → Burning flag) lose Integrity (BurnDamagePerSecond) and burn down;
    ///   • they heat flammable neighbours (FlammableData: SpreadRadius of the source, IgnitionTime of the
    ///     target); at heat 1 the neighbour catches fire. Wet items don't heat up and don't ignite;
    ///   • the sea puts out (and soaks) items under water and fills buckets dipped in it;
    ///   • powder (ExplosiveData) that burns runs its fuse and explodes: damage + fire + push to items,
    ///     knockout / shove to crew; a powder barrel that breaks while burning explodes at once (chains);
    ///   • a lantern (LanternData) that breaks sets flammable items around it on fire.
    /// Bucket splash / refill come from the crew (NetworkPlayer → <see cref="Splash"/>, <see cref="Fill"/>).
    /// </summary>
    public sealed class FireDirector : SimulationBehaviour
    {
        public static FireDirector Instance { get; private set; }

        [SerializeField] private FireConfig config;

        private FireConfig _runtime;
        private NetworkRunner _registeredOn;
        private OceanWaveField _ocean;
        private readonly Dictionary<WorldItem, float> _heat = new();
        private readonly Dictionary<WorldItem, float> _fuseLit = new();   // simulation time the fuse was lit
        private readonly HashSet<WorldItem> _brokenSeen = new();
        private readonly List<WorldItem> _burning = new();
        private readonly List<WorldItem> _scratch = new();

        /// <summary>Live tuning (a runtime copy of the asset; F9 edits it).</summary>
        public FireConfig Config => _runtime != null ? _runtime : _runtime = MakeRuntime();
        public FireConfig Asset => config;

        // Host debug counters.
        public int Ignitions { get; private set; }
        public int Extinguished { get; private set; }
        public int Explosions { get; private set; }
        public int Splashes { get; private set; }

        public float HeatOf(WorldItem item) => item != null && _heat.TryGetValue(item, out float h) ? h : 0f;

        /// <summary>Seconds left on a lit fuse, or -1.</summary>
        public float FuseLeft(WorldItem item)
        {
            if (item == null || !_fuseLit.TryGetValue(item, out float lit) || Runner == null) return -1f;
            ExplosiveData data = item.Attributes.GetData<ExplosiveData>();
            return data == null ? -1f : Mathf.Max(0f, data.FuseTime - (Runner.SimulationTime - lit));
        }

        private void Awake()
        {
            Instance = this;
            _runtime = MakeRuntime();
        }

        private FireConfig MakeRuntime()
        {
            FireConfig copy = FireConfig.CreateDefault();
            copy.CopyFrom(config);
            copy.name = config != null ? $"{config.name} (runtime)" : copy.name;
            return copy;
        }

        public void ResetConfig() => Config.CopyFrom(config != null ? config : FireConfig.CreateDefault());

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_registeredOn != null && _registeredOn.IsRunning) _registeredOn.RemoveGlobal(this);
        }

        private void Update()
        {
            if (_registeredOn != null && _registeredOn.IsRunning) return;
            _registeredOn = null;
            foreach (NetworkRunner r in NetworkRunner.Instances)
            {
                if (r == null || !r.IsRunning || !r.IsServer) continue;
                r.AddGlobal(this);
                _registeredOn = r;
                break;
            }
        }

        // ───────────────────────── tick ─────────────────────────

        public override void FixedUpdateNetwork()
        {
            if (Runner == null || !Runner.IsServer) return;
            if (_ocean == null) _ocean = FindAnyObjectByType<OceanWaveField>();
            float dt = Runner.DeltaTime;
            FireConfig cfg = Config;

            HandleBreaks();

            _burning.Clear();
            foreach (WorldItem item in WorldItem.All)
            {
                if (!Live(item)) continue;
                AttributesComponent a = item.Attributes;
                float submerged = Submerged(item);

                if (item.Attributes.GetData<BucketData>() != null && submerged >= cfg.SubmergedToQuench && a.Has(Attr.Water))
                    a.Set.SetCurrent(Attr.Water, a.Get(Attr.Water));

                if (!a.HasFlag(AttrFlag.Burning)) continue;
                if (a.HasFlag(AttrFlag.Wet) || submerged >= cfg.SubmergedToQuench)
                {
                    Extinguish(item, soak: true);
                    continue;
                }
                _burning.Add(item);
            }

            foreach (WorldItem item in _burning)
            {
                AttributeSet set = item.Attributes.Set;
                if (set.Has(Attr.Integrity))
                    set.AddCurrent(Attr.Integrity, -cfg.BurnDamagePerSecond * dt);

                ExplosiveData explosive = item.Attributes.GetData<ExplosiveData>();
                if (explosive != null)
                {
                    if (!_fuseLit.TryGetValue(item, out float lit)) _fuseLit[item] = lit = Runner.SimulationTime;
                    if (Runner.SimulationTime - lit >= explosive.FuseTime)
                        Explode(item, explosive);
                }
            }

            Spread(cfg, dt);
            CleanUp();
        }

        private void Spread(FireConfig cfg, float dt)
        {
            foreach (WorldItem target in WorldItem.All)
            {
                if (!Live(target) || !CanIgnite(target))
                {
                    _heat.Remove(target);
                    continue;
                }

                FlammableData tf = target.Attributes.GetData<FlammableData>();
                float ignition = tf != null ? tf.IgnitionTime : cfg.DefaultIgnitionTime;
                Bounds tb = target.WorldBounds;
                float rate = 0f;
                foreach (WorldItem source in _burning)
                {
                    if (source == target || !Live(source)) continue;
                    FlammableData sf = source.Attributes.GetData<FlammableData>();
                    float radius = sf != null ? sf.SpreadRadius : cfg.DefaultSpreadRadius;
                    rate += FireRules.HeatRate(Gap(source.WorldBounds, tb), radius, ignition, cfg.SpreadMultiplier);
                }

                float heat = _heat.TryGetValue(target, out float h) ? h : 0f;
                if (rate <= 0f && heat <= 0f) continue;
                heat = FireRules.StepHeat(heat, rate, cfg.HeatDecayPerSecond, dt);
                if (FireRules.Ignites(heat)) Ignite(target);
                else if (heat > 0f) _heat[target] = heat;
                else _heat.Remove(target);
            }
        }

        /// <summary>Items that broke since last tick: lanterns spill fire, burning powder explodes.</summary>
        private void HandleBreaks()
        {
            foreach (WorldItem item in WorldItem.All)
            {
                if (item == null || item.Object == null || !item.Object.IsValid || !item.Broken || !_brokenSeen.Add(item))
                    continue;
                AttributesComponent a = item.Attributes;
                ExplosiveData explosive = a.GetData<ExplosiveData>();
                if (explosive != null && a.HasFlag(AttrFlag.Burning) && !a.HasFlag(AttrFlag.Exploded))
                {
                    Explode(item, explosive);
                    continue;
                }
                LanternData lantern = a.GetData<LanternData>();
                if (lantern != null)
                    IgniteAround(item.WorldBounds.center, lantern.IgniteRadius, item);
            }
        }

        private void CleanUp()
        {
            _scratch.Clear();
            foreach (WorldItem w in _brokenSeen) if (w == null || w.Object == null || !w.Object.IsValid) _scratch.Add(w);
            foreach (WorldItem w in _scratch) { _brokenSeen.Remove(w); _heat.Remove(w); _fuseLit.Remove(w); }

            _scratch.Clear();
            foreach (KeyValuePair<WorldItem, float> kv in _fuseLit)
                if (!Live(kv.Key) || !kv.Key.Attributes.HasFlag(AttrFlag.Burning)) _scratch.Add(kv.Key);
            foreach (WorldItem w in _scratch) _fuseLit.Remove(w);
        }

        // ───────────────────────── actions (host) ─────────────────────────

        public static bool CanIgnite(WorldItem item) =>
            item != null && !item.Broken && !item.IsStored && item.Attributes.Set != null &&
            item.Attributes.HasFlag(AttrFlag.Flammable) && !item.Attributes.HasFlag(AttrFlag.Burning) &&
            !item.Attributes.HasFlag(AttrFlag.Wet);

        /// <summary>Host: set on fire (flammable, dry, not already burning).</summary>
        public bool Ignite(WorldItem item)
        {
            if (!IsHost(item) || !CanIgnite(item)) return false;
            if (!item.Attributes.ApplyEffect(AttrEffect.OnFireFx)) return false;
            _heat.Remove(item);
            Ignitions++;
            return true;
        }

        /// <summary>Host: put out; <paramref name="soak"/> also makes it Wet (WetFx — won't catch fire again for a while).</summary>
        public bool Extinguish(WorldItem item, bool soak)
        {
            if (!IsHost(item)) return false;
            AttributesComponent a = item.Attributes;
            bool wasBurning = a.HasFlag(AttrFlag.Burning);
            a.RemoveEffect(AttrEffect.OnFireFx);
            a.RemoveEffect(AttrEffect.BurningFx);
            if (soak) a.ApplyEffect(AttrEffect.WetFx);
            _heat.Remove(item);
            _fuseLit.Remove(item);
            if (wasBurning) Extinguished++;
            return wasBurning;
        }

        public void IgniteAround(Vector3 centre, float radius, WorldItem except = null)
        {
            foreach (WorldItem other in WorldItem.All)
            {
                if (other == except || !Live(other)) continue;
                if (Gap(other.WorldBounds, centre) <= radius) Ignite(other);
            }
        }

        /// <summary>Host: blow up a powder item now (sets Exploded so every peer shows the blast, then breaks it).</summary>
        public void Explode(WorldItem item, ExplosiveData data = null)
        {
            if (item == null || item.Object == null || !item.Object.IsValid || !item.HasStateAuthority) return;
            AttributesComponent a = item.Attributes;
            data ??= a.GetData<ExplosiveData>() ?? new ExplosiveData();
            if (a.HasFlag(AttrFlag.Exploded)) return;
            a.Set.SetFlag(AttrFlag.Exploded, true);
            _fuseLit.Remove(item);
            Explosions++;
            Vector3 centre = item.WorldBounds.center;

            foreach (WorldItem other in WorldItem.All)
            {
                if (other == item || !Live(other)) continue;
                Bounds b = other.WorldBounds;
                float d = Gap(b, centre);
                float f = FireRules.BlastFalloff(d, data.Radius);
                if (f <= 0f) continue;

                AttributesComponent oa = other.Attributes;
                // Powder in the blast always catches (chain reaction); other flammables only close in.
                if (oa.GetData<ExplosiveData>() != null || d <= data.Radius * data.IgniteFraction)
                    Ignite(other);
                if (oa.Has(Attr.Integrity))
                    oa.Set.AddCurrent(Attr.Integrity, -data.Damage * f);

                Rigidbody rb = other.Body;
                if (rb != null && !rb.isKinematic)
                {
                    Vector3 dir = (b.center - centre + Vector3.up * 0.5f).normalized;
                    rb.AddForce(dir * (data.ItemPush * f), ForceMode.VelocityChange);
                }
            }

            foreach (NetworkPlayer p in NetworkPlayer.All)
            {
                if (p == null || p.Object == null || !p.Object.IsValid || !p.HasStateAuthority) continue;
                Vector3 chest = p.transform.position + Vector3.up;
                float d = Vector3.Distance(chest, centre);
                float f = FireRules.BlastFalloff(d, data.Radius);
                if (f <= 0f) continue;
                Vector3 dir = (chest - centre + Vector3.up * 0.6f).normalized;
                p.Shove(dir * (data.CrewPush * f), d <= data.KnockoutRadius ? 1f : 0.5f * f);
            }

            item.Break();
            _brokenSeen.Add(item);
        }

        /// <summary>
        /// Host: splash the bucket's water <see cref="BucketData.SplashDistance"/> ahead of <paramref name="origin"/>:
        /// puts out and soaks what's there. Spends <see cref="FireConfig.WaterPerSplash"/>; false if empty / not a bucket.
        /// </summary>
        public static bool Splash(WorldItem bucket, Vector3 origin, Vector3 forward)
        {
            if (!IsHost(bucket)) return false;
            BucketData data = bucket.Attributes.GetData<BucketData>();
            AttributeSet set = bucket.Attributes.Set;
            if (data == null || set == null || !set.Has(Attr.Water)) return false;
            FireDirector director = Instance;
            float cost = director != null ? director.Config.WaterPerSplash : 1f;
            if (set.GetCurrent(Attr.Water) < cost - 1e-3f) return false;
            set.AddCurrent(Attr.Water, -cost);

            Vector3 centre = FireRules.SplashCentre(origin, forward, data.SplashDistance);
            foreach (WorldItem other in WorldItem.All)
            {
                if (other == bucket || !Live(other)) continue;
                Vector3 closest = other.WorldBounds.ClosestPoint(centre);
                if (!FireRules.InSplash(closest, centre, data.SplashRadius)) continue;
                if (director != null) director.Extinguish(other, soak: true);
                else { other.Attributes.RemoveEffect(AttrEffect.OnFireFx); other.Attributes.ApplyEffect(AttrEffect.WetFx); }
            }
            if (director != null) director.Splashes++;
            return true;
        }

        /// <summary>Host: fill <paramref name="bucket"/> from a water source (cask). False if nothing moved.</summary>
        public static bool Fill(WorldItem bucket, WorldItem source)
        {
            if (!IsHost(bucket) || source == null || source.Broken) return false;
            AttributeSet b = bucket.Attributes.Set;
            WaterSourceData src = source.Attributes.GetData<WaterSourceData>();
            if (bucket.Attributes.GetData<BucketData>() == null || src == null || b == null || !b.Has(Attr.Water)) return false;
            float missing = b.Get(Attr.Water) - b.GetCurrent(Attr.Water);
            if (missing <= 1e-3f) return false;
            float moved = missing;
            if (!src.Infinite)
            {
                AttributeSet s = source.Attributes.Set;
                if (s == null || !s.Has(Attr.Water)) return false;
                moved = Mathf.Min(missing, s.GetCurrent(Attr.Water));
                if (moved <= 1e-3f) return false;
                s.AddCurrent(Attr.Water, -moved);
            }
            b.AddCurrent(Attr.Water, moved);
            return true;
        }

        // ───────────────────────── helpers ─────────────────────────

        private static bool IsHost(WorldItem item) =>
            item != null && item.Object != null && item.Object.IsValid && item.HasStateAuthority && item.Attributes.Set != null;

        private static bool Live(WorldItem item) =>
            item != null && item.Object != null && item.Object.IsValid && !item.Broken && !item.IsStored && item.Attributes.Set != null;

        private float Submerged(WorldItem item)
        {
            if (_ocean == null) return 0f;
            Bounds b = item.WorldBounds;
            return ItemRules.SubmergedFraction(_ocean.SampleHeight(b.center), b.min.y, b.size.y);
        }

        /// <summary>Gap (m) between two boxes (0 when touching / overlapping).</summary>
        private static float Gap(Bounds a, Bounds b) => Mathf.Sqrt(b.SqrDistance(a.ClosestPoint(b.center)));

        private static float Gap(Bounds a, Vector3 point) => Mathf.Sqrt(a.SqrDistance(point));
    }
}
#endif
