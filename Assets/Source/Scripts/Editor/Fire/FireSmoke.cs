#if FUSION2
using System.Collections.Generic;
using System.Text;
using Fusion;
using RumOverboard.EditorTools.Items;
using RumOverboard.Gameplay.Attributes;
using RumOverboard.Gameplay.Fire;
using RumOverboard.Gameplay.Interaction;
using RumOverboard.Gameplay.Items;
using RumOverboard.Gameplay.Ocean;
using RumOverboard.Networking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RumOverboard.EditorTools.Fire
{
    /// <summary>
    /// Play-mode scenario on FireScene with the real crew member (host, injected input, frozen ship, fresh test items):
    /// fire spreads to a crate next to a burning one but not to a far one → Q with a bucket puts both out and soaks
    /// them (−1 water, splash + steam shown) → a wet crate won't ignite → E on the water cask refills the bucket →
    /// a bucket in the sea fills up, a burning board in the sea goes out → a breaking lantern sets a crate on fire →
    /// a burning crate burns down → a burning powder barrel explodes after its fuse: damages, ignites and pushes a
    /// crate, knocks out the crew member, every peer sees the blast.
    /// Menu: RumOverboard ▸ Fire ▸ Run Smoke (Play Mode). Logs [FireSmoke] PASS/FAIL.
    /// </summary>
    public static class FireSmoke
    {
        private const string PendingKey = "RumOverboard.FireSmoke.Pending";

        private enum Step { WaitSpawn, Spread, Splash, WetNoIgnite, Refill, Sea, Lantern, Burnout, Powder, Done }

        private static Step _step;
        private static double _stepStart, _settledSince = -1;
        private static NetworkPlayer _player;
        private static NetworkInputData _input;
        private static readonly StringBuilder Report = new();
        private static int _failures;
        private static bool _entered;
        private static FireDirector _director;
        private static FireVisuals _visuals;
        private static WorldItem _a, _b, _far, _bucket, _cask, _seaBucket, _seaBoard, _lantern, _victim, _powder;
        private static readonly List<WorldItem> Spawned = new();
        private static float _mark, _mark2, _peak, _blastDistance;
        private static int _count, _count2;
        private static Vector3 _pos;

        [MenuItem("RumOverboard/Fire/Run Smoke (Play Mode)", priority = 21)]
        public static void Run()
        {
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(FireSetup.ScenePath);
            SessionState.SetBool(PendingKey, true);
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        private static void OnPlayMode(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false)) return;
            Report.Clear();
            Spawned.Clear();
            _failures = 0;
            _player = null;
            _settledSince = -1;
            Go(Step.WaitSpawn);
        }

        private static bool Once()
        {
            if (_entered) return false;
            _entered = true;
            return true;
        }

        private static void Update()
        {
            if (!SessionState.GetBool(PendingKey, false) || !EditorApplication.isPlaying) return;
            double now = EditorApplication.timeSinceStartup;
            double t = now - _stepStart;

            if (_step == Step.WaitSpawn)
            {
                foreach (NetworkPlayer p in NetworkPlayer.All)
                    if (p != null && p.HasStateAuthority && p.HasInputAuthority && p.Attributes != null && p.Attributes.IsAuthority)
                        _player = p;
                _director = FireDirector.Instance;
                _visuals = FireVisuals.Instance;
                var spawner = Object.FindAnyObjectByType<ItemSpawner>();
                bool settled = _player != null && !_player.IsInAir && !_player.IsSwimming && spawner != null && spawner.Done &&
                               _director != null && _director.Runner != null && _visuals != null;
                if (!settled) _settledSince = -1;
                else if (_settledSince < 0) _settledSince = now;
                if (!settled || now - _settledSince < 2.0)
                {
                    if (t > 120.0) Finish($"crew on deck, items spawned, fire director registered within 120 s (director {(_director != null)}, " +
                                          $"runner {(_director != null && _director.Runner != null)}, visuals {(_visuals != null)})", false);
                    return;
                }
                Check(true, $"spawned: {WorldItem.All.Count} items, director on the host runner");
                // The mechanics are tested on fresh items: clear the scene's layout off the deck, and face in
                // towards the centreline (the crew spawns by the rail — "1.6 m ahead" would be over the side).
                var sceneItems = new List<WorldItem>(WorldItem.All);
                foreach (WorldItem i in sceneItems) Despawn(i);
                NetworkShip deckShip = NetworkShip.All.Count > 0 ? NetworkShip.All[0] : null;
                float inward = deckShip != null && deckShip.transform.InverseTransformPoint(_player.transform.position).x < 0f ? 50f : -50f;
                _input = default;
                _input.LookYaw = (deckShip != null ? deckShip.transform.eulerAngles.y : _player.ViewYaw) + inward;
                _player.InputOverride = () => _input;
                FreezeShip(true);
                Go(Step.Spread);
                return;
            }

            if (_player == null || _player.Object == null || !_player.Object.IsValid)
            {
                Finish("player still alive", false);
                return;
            }

            switch (_step)
            {
                case Step.Spread:
                    if (t < 1.0) return; // turned to the new yaw
                    if (Once())
                    {
                        // Kinematic test crates where the bucket's splash will land: exact gaps, no physics in the way.
                        Vector3 centre = SplashCentre() + Vector3.up * 0.4f;
                        Vector3 right = Quaternion.Euler(0f, _player.ViewYaw, 0f) * Vector3.right;
                        _a = Spawn("Crate", centre - right * 0.45f, kinematic: true);
                        _b = Spawn("Crate", centre + right * 0.45f, kinematic: true);     // gap 0.3 m
                        _far = Spawn("Crate", centre + right * 4.5f + Vector3.up * 3f, kinematic: true);
                        _count = _visuals.FlamesShown;
                    }
                    if (t < 0.5) return;
                    if (_a == null || _b == null || _far == null) { Finish("test crates spawned", false); return; }
                    if (!Burning(_a))
                    {
                        Check(_director.Ignite(_a), "Ignite() sets a dry crate on fire");
                        _mark = _a.Attributes.GetCurrent(Attr.Integrity);
                        _stepStart = EditorApplication.timeSinceStartup;
                        return;
                    }
                    if (!Burning(_b) && t < 9.0) return;
                    Check(Burning(_b), $"fire spreads to the crate 0.3 m away ({t:0.0} s, heat {_director.HeatOf(_b):0.00})");
                    Check(!Burning(_far) && _director.HeatOf(_far) <= 0f, "…but not to the crate 4.5 m away");
                    Check(_a.Attributes.GetCurrent(Attr.Integrity) < _mark - 4f,
                          $"burning costs integrity ({_mark:0} → {_a.Attributes.GetCurrent(Attr.Integrity):0.0})");
                    Check(_visuals.FlamesShown >= 2, $"flames shown on both crates ({_visuals.FlamesShown})");
                    Go(Step.Splash);
                    return;

                case Step.Splash:
                    if (Once())
                    {
                        _bucket = Spawn("Bucket", _player.transform.position + Vector3.up * 1.2f, kinematic: false);
                        _player.DebugGiveItem(_bucket);
                        _mark = _bucket.Attributes.GetCurrent(Attr.Water);
                        _count = _visuals.SplashesShown;
                        _count2 = _visuals.SteamShown;
                        _mark2 = _director.Splashes;
                    }
                    if (t < 0.6) return;
                    if (!Act(NetworkInputData.ButtonDrink, t, () => !Burning(_a) && !Burning(_b), timeout: 2.0)) return;
                    if (t < 1.6) return; // let the visuals see it
                    Check(_player.CarriedId == _bucket.Object.Id, "bucket in hands");
                    Check(!Burning(_a) && !Burning(_b), "Q with a bucket puts both crates out");
                    Check(Wet(_a) && Wet(_b), "…and soaks them (Wet)");
                    Check(Mathf.Approximately(_bucket.Attributes.GetCurrent(Attr.Water), _mark - 1f),
                          $"one splash of water spent ({_mark:0} → {_bucket.Attributes.GetCurrent(Attr.Water):0})");
                    Check(_director.Splashes == (int)_mark2 + 1, "exactly one splash");
                    Check(_visuals.SplashesShown > _count && _visuals.SteamShown >= _count2 + 2,
                          $"splash + steam shown (splashes {_visuals.SplashesShown - _count}, steam {_visuals.SteamShown - _count2})");
                    Go(Step.WetNoIgnite);
                    return;

                case Step.WetNoIgnite:
                    Check(!_director.Ignite(_a) && !Burning(_a), "a wet crate doesn't catch fire");
                    Go(Step.Refill);
                    return;

                case Step.Refill:
                    if (Once())
                    {
                        Vector3 fwd = Quaternion.Euler(0f, _player.ViewYaw, 0f) * Vector3.forward;
                        _cask = Spawn("WaterCask", _player.transform.position + fwd * 1.2f + Vector3.up * 2.4f, kinematic: true);
                        _mark = _cask != null ? _cask.Attributes.GetCurrent(Attr.Water) : 0f;
                    }
                    if (t < 0.5) return;
                    if (_cask == null) { Finish("water cask spawned", false); return; }
                    if (!Act(NetworkInputData.ButtonInteract, t, () => Full(_bucket), Ref(_cask))) return;
                    Check(Full(_bucket), $"E on the water cask fills the bucket ({_bucket.Attributes.GetCurrent(Attr.Water):0}/{_bucket.Attributes.Get(Attr.Water):0})");
                    Check(Mathf.Abs(_cask.Attributes.GetCurrent(Attr.Water) - (_mark - 1f)) < 0.01f,
                          $"…from the cask's water ({_mark:0} → {_cask.Attributes.GetCurrent(Attr.Water):0})");
                    Go(Step.Sea);
                    return;

                case Step.Sea:
                    if (Once())
                    {
                        Vector3 sea = SeaSpot();
                        _seaBucket = Spawn("Bucket", sea, kinematic: false);
                        _seaBoard = Spawn("Board", sea + Vector3.forward * 2.5f, kinematic: false);
                        if (_seaBucket != null) _seaBucket.Attributes.Set.SetCurrent(Attr.Water, 0f);
                        if (_seaBoard != null) _director.Ignite(_seaBoard);
                    }
                    if (t < 3.0) return;
                    Check(_seaBucket != null && Full(_seaBucket), "a bucket in the sea fills up");
                    Check(_seaBoard != null && !Burning(_seaBoard) && Wet(_seaBoard), "a burning board in the sea goes out (and is wet)");
                    Go(Step.Lantern);
                    return;

                case Step.Lantern:
                    if (Once())
                    {
                        Despawn(_a); Despawn(_b); Despawn(_far);
                        Vector3 spot = _player.transform.position + Vector3.up * 3.5f;
                        _lantern = Spawn("Lantern", spot, kinematic: true);
                        _victim = Spawn("Crate", spot + Vector3.right * 0.9f, kinematic: true);
                    }
                    if (t < 0.5) return;
                    if (_lantern == null || _victim == null) { Finish("lantern + crate spawned", false); return; }
                    if (!_lantern.Broken) { _lantern.Break(); return; }
                    if (!Burning(_victim) && t < 2.0) return;
                    Check(Burning(_victim), "a breaking lantern sets the crate next to it on fire");
                    Go(Step.Burnout);
                    return;

                case Step.Burnout:
                    if (Once()) _victim.Attributes.Set.SetCurrent(Attr.Integrity, 0.5f);
                    if (Alive(_victim) && t < 2.5) return;
                    Check(!Alive(_victim) || _victim.Broken, "a burning crate at the end of its integrity burns down (breaks)");
                    Go(Step.Powder);
                    return;

                case Step.Powder:
                    if (Once())
                    {
                        Despawn(_cask);
                        Vector3 at = DeckSpot(1.5f, 0.7f, 0.5f);
                        Vector3 away = at - _player.transform.position;
                        away.y = 0f;
                        _powder = Spawn("PowderBarrel", at, kinematic: false);
                        _victim = Spawn("Crate", at + away.normalized * 2.2f + Vector3.up * 0.3f, kinematic: false); // ~half blast: survives
                        _count = _visuals.ExplosionsShown;
                        _mark2 = _director.Explosions;
                        _peak = 0f;
                        if (_powder == null || _victim == null) { Finish("powder + crate spawned", false); return; }
                    }
                    if (t < 1.0) return;
                    if (Alive(_powder) && !Burning(_powder))
                    {
                        Check(_director.Ignite(_powder), "powder barrel catches fire");
                        _mark = _victim.Attributes.GetCurrent(Attr.Integrity);
                        _pos = _victim.transform.position;
                        _stepStart = EditorApplication.timeSinceStartup;
                        return;
                    }
                    _peak = Mathf.Max(_peak, _player.RagdollControl);
                    if (Alive(_powder))
                    {
                        _blastDistance = Vector3.Distance(_powder.WorldBounds.center, _player.transform.position + Vector3.up);
                        if (t > 6.0) { Finish($"powder exploded within 6 s (fuse left {_director.FuseLeft(_powder):0.0})", false); return; }
                        return;
                    }
                    if (t < 4.0) return; // see the aftermath
                    Check(_director.Explosions == (int)_mark2 + 1, $"one explosion after the fuse ({_director.Explosions - (int)_mark2})");
                    Check(_visuals.ExplosionsShown > _count, "blast shown");
                    Check(_peak >= 0.85f, $"crew member {_blastDistance:0.0} m from the blast knocked out (ragdoll peak {_peak:0.00})");
                    if (Alive(_victim))
                    {
                        Check(_victim.Attributes.GetCurrent(Attr.Integrity) < _mark - 10f,
                              $"crate in the blast damaged ({_mark:0} → {_victim.Attributes.GetCurrent(Attr.Integrity):0})");
                        Check(Burning(_victim) || Wet(_victim) || _victim.Broken, "…and set on fire");
                        Check(Vector3.Distance(_victim.transform.position, _pos) > 0.3f,
                              $"…and pushed ({Vector3.Distance(_victim.transform.position, _pos):0.00} m)");
                    }
                    else Check(true, "crate in the blast destroyed");
                    Finish(null, true);
                    return;
            }
        }

        // ───────────────────────── helpers ─────────────────────────

        private static Vector3 SplashCentre()
        {
            BucketData data = new BucketData();
            return FireRules.SplashCentre(_player.transform.position, Quaternion.Euler(0f, _player.ViewYaw, 0f) * Vector3.forward, data.SplashDistance);
        }

        private static Vector3 SeaSpot()
        {
            NetworkShip ship = NetworkShip.All.Count > 0 ? NetworkShip.All[0] : null;
            var ocean = Object.FindAnyObjectByType<OceanWaveField>();
            Vector3 p = ship != null ? ship.transform.position + ship.transform.right * 9f : _player.transform.position + Vector3.right * 9f;
            if (ocean != null) p.y = ocean.SampleHeight(p) + 0.3f;
            return p;
        }

        private static bool Alive(WorldItem i) => i != null && i.Object != null && i.Object.IsValid && !i.Broken;
        private static bool Burning(WorldItem i) => Alive(i) && i.Attributes.HasFlag(AttrFlag.Burning);
        private static bool Wet(WorldItem i) => Alive(i) && i.Attributes.HasFlag(AttrFlag.Wet);
        private static bool Full(WorldItem i) => Alive(i) && i.Attributes.GetCurrent(Attr.Water) >= i.Attributes.Get(Attr.Water) - 0.01f;

        private static WorldItem Spawn(string id, Vector3 pos, bool kinematic)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(ItemsSetup.CatalogPath);
            GameObject prefab = catalog != null ? catalog.Get(id) : null;
            if (prefab == null) return null;
            NetworkObject obj = _player.Runner.Spawn(prefab, pos, Quaternion.Euler(0f, _player.ViewYaw, 0f));
            WorldItem item = obj != null ? obj.GetComponent<WorldItem>() : null;
            if (item == null) return null;
            if (kinematic) item.Body.isKinematic = true;
            Spawned.Add(item);
            return item;
        }

        private static void Despawn(WorldItem i)
        {
            if (i != null && i.Object != null && i.Object.IsValid) _player.Runner.Despawn(i.Object);
        }

        private static InteractableRef Ref(WorldItem item)
        {
            if (item == null) return default;
            var i = item.GetComponent<Interactable>();
            return i != null ? i.Ref : default;
        }

        /// <summary>Release the button ≥ 0.3 s, then hold it until <paramref name="done"/> (or the timeout).</summary>
        private static bool Act(int button, double t, System.Func<bool> done, InteractableRef target = default, double timeout = 3.0)
        {
            _input.Target = target;
            _input.LookYaw = _player.ViewYaw;
            if (t < 0.3)
            {
                _input.Buttons.Set(button, false);
                return false;
            }
            bool finished = done() || t > 0.3 + timeout;
            _input.Buttons.Set(button, !finished);
            return finished;
        }

        private static readonly Collider[] Overlaps = new Collider[32];

        /// <summary>Free deck point <paramref name="distance"/> from the crew member (view direction first).</summary>
        private static Vector3 DeckSpot(float distance, float height, float radius)
        {
            Vector3 fallback = _player.transform.position + Quaternion.Euler(0f, _player.ViewYaw, 0f) * Vector3.forward * distance + Vector3.up * height;
            for (int a = 0; a < 16; a++)
            {
                float angle = (a % 2 == 0 ? 1f : -1f) * ((a + 1) / 2) * 22.5f;
                Vector3 dir = Quaternion.Euler(0f, _player.ViewYaw + angle, 0f) * Vector3.forward;
                Vector3 candidate = _player.transform.position + dir * distance + Vector3.up * height;
                if (IsFree(candidate, radius) && OverDeck(candidate, height + 2f)) return candidate;
            }
            return fallback;
        }

        private static bool OverDeck(Vector3 point, float maxDrop) =>
            Physics.Raycast(point, Vector3.down, out RaycastHit hit, maxDrop, ~0, QueryTriggerInteraction.Ignore) &&
            hit.collider.GetComponentInParent<NetworkShip>() != null;

        private static bool IsFree(Vector3 center, float radius)
        {
            int n = Physics.OverlapSphereNonAlloc(center, radius, Overlaps, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (!Overlaps[i].transform.IsChildOf(_player.transform)) return false;
            return true;
        }

        private static RigidbodyConstraints _shipConstraints;

        private static void FreezeShip(bool freeze)
        {
            foreach (NetworkShip ship in NetworkShip.All)
            {
                if (ship == null || ship.Body == null) continue;
                if (freeze)
                {
                    _shipConstraints = ship.Body.constraints;
                    ship.Body.linearVelocity = Vector3.zero;
                    ship.Body.angularVelocity = Vector3.zero;
                    ship.Body.constraints = RigidbodyConstraints.FreezeAll;
                }
                else ship.Body.constraints = _shipConstraints;
            }
        }

        private static void Go(Step step)
        {
            _step = step;
            _stepStart = EditorApplication.timeSinceStartup;
            _entered = false;
            _input.Buttons = default;
            _input.Target = default;
        }

        private static bool Check(bool ok, string what)
        {
            Report.AppendLine($"  {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) _failures++;
            return ok;
        }

        private static void Finish(string failure, bool completed)
        {
            if (failure != null) Check(false, failure);
            if (!completed) Check(false, $"scenario stopped at step {_step}");
            SessionState.SetBool(PendingKey, false);
            if (_player != null) _player.InputOverride = null;
            FreezeShip(false);
            _step = Step.Done;
            string text = $"[FireSmoke] {(_failures == 0 ? "PASS" : $"{_failures} FAILED")}\n{Report}";
            if (_failures == 0) Debug.Log(text);
            else Debug.LogError(text);
            EditorApplication.isPlaying = false;
        }
    }
}
#endif
