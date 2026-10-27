#if FUSION2
using System.Text;
using Fusion;
using RumOverboard.Gameplay.Attributes;
using RumOverboard.Gameplay.Crew;
using RumOverboard.Gameplay.Interaction;
using RumOverboard.Gameplay.Items;
using RumOverboard.Gameplay.Ocean;
using RumOverboard.Networking;
using RumOverboard.StateMachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RumOverboard.EditorTools.Items
{
    /// <summary>
    /// Play-mode scenario on ItemsScene with the real crew member (host, injected input — items are
    /// placed in front of the crew member, the Target is the one the HUD would send):
    /// pick up a bottle → sip (−1 sip, +intoxication, +grog) → store it in a chest → take it out → put it
    /// down → throw it → lift the powder barrel at full capacity (slower, no jump) → too heavy item →
    /// a chest holding the compass breaks and spills it → impact damage → a barrel floats → no sip without
    /// a bottle. Menu: RumOverboard ▸ Items ▸ Run Smoke (Play Mode). Logs [ItemsSmoke] PASS/FAIL.
    /// </summary>
    public static class ItemsSmoke
    {
        private const string PendingKey = "RumOverboard.ItemsSmoke.Pending";

        private enum Step
        {
            WaitSpawn, PickBottle, Carry, Drink, SpawnChest, Store, Take, PutDown, PickAgain, Throw,
            HeavyPick, HeavyWalk, HeavyDrop, TooHeavy, CompassPick, CompassStore, BreakChest, Impact, Float, NoBottle, Done
        }

        private static Step _step;
        private static double _stepStart, _settledSince = -1;
        private static NetworkPlayer _player;
        private static NetworkInputData _input;
        private static readonly StringBuilder Report = new();
        private static int _failures;
        private static WorldItem _bottle, _chest, _powder, _compass, _crate, _floatBarrel;
        private static float _mark, _mark2, _maxSpeed;
        private static int _jumps0, _speedCount;
        private static float _speedSum;
        private static bool _flag;
        private static NetworkId _lastCarried;
        private static bool _entered; // first update of the current step (editor updates are irregular when unfocused)

        private static bool Once()
        {
            if (_entered) return false;
            _entered = true;
            return true;
        }

        [MenuItem("RumOverboard/Items/Run Smoke (Play Mode)", priority = 21)]
        public static void Run()
        {
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ItemsSetup.ScenePath);
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
            _failures = 0;
            _player = null;
            _settledSince = -1;
            _bottle = _chest = _powder = _compass = _crate = _floatBarrel = null;
            _lastCarried = default;
            Go(Step.WaitSpawn);
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
                var spawner = Object.FindAnyObjectByType<ItemSpawner>();
                bool settled = _player != null && !_player.IsInAir && !_player.IsSwimming && spawner != null && spawner.Done &&
                               WorldItem.All.Count >= 12;
                if (!settled) _settledSince = -1;
                else if (_settledSince < 0) _settledSince = now;
                if (!settled || now - _settledSince < 2.0)
                {
                    if (t > 120.0) Finish($"crew member on deck + 12 items spawned within 120 s (items {WorldItem.All.Count}, spawner done {(spawner != null && spawner.Done)}, " +
                                          $"player {(_player != null ? $"{_player.ActiveStates} at {_player.transform.position}, ship at {(NetworkShip.All.Count > 0 ? NetworkShip.All[0].transform.position.ToString() : "-")}" : "none")})", false);
                    return;
                }
                Check(true, $"spawned: {WorldItem.All.Count} items");
                _input = default;
                _player.InputOverride = () => _input;
                // The scene layout is checked above; the mechanics are tested on fresh items on a frozen deck
                // (a drifting, rolling brig among rigging and loose loot makes placement non-deterministic).
                _floatBarrel = FindFarthest("Barrel");
                FreezeShip(true);
                _bottle = SpawnTestItem("GrogBottle");
                _powder = SpawnTestItem("PowderBarrel");
                _compass = SpawnTestItem("Compass");
                _crate = SpawnTestItem("Crate");
                if (_bottle == null || _powder == null || _compass == null || _crate == null || _floatBarrel == null)
                {
                    Finish($"scene items found (bottle {_bottle != null}, powder {_powder != null}, compass {_compass != null}, " +
                           $"crate {_crate != null}, water barrel {_floatBarrel != null})", false);
                    return;
                }
                Go(Step.PickBottle);
                return;
            }

            if (_player == null || _player.Object == null || !_player.Object.IsValid)
            {
                Finish("player still alive", false);
                return;
            }

            AttributeSet me = _player.Attributes.Set;
            _maxSpeed = Mathf.Max(_maxSpeed, _player.PlanarSpeed);
            if (!_player.IsInAir && t > 0.3) { _speedSum += _player.PlanarSpeed; _speedCount++; }
            bool carrying = (_player.ActiveStates & PlayerState.Carrying) != 0;
            if (_player.CarriedId != _lastCarried)
            {
                Trace($"{_step} t={t:0.00}: in hands → {NameOf(_player.CarriedId)}");
                _lastCarried = _player.CarriedId;
            }

            switch (_step)
            {
                case Step.PickBottle:
                    if (Once()) PlaceInFront(_bottle);
                    if (!Act(NetworkInputData.ButtonInteract, t, () => _player.CarriedId == _bottle.Object.Id, Ref(_bottle))) return;
                    Check(_player.CarriedId == _bottle.Object.Id && _bottle.Holder == _player.Object.InputAuthority,
                          $"E on a bottle → in hands (holder {_bottle.Holder}, " +
                          $"eye→bottle {Vector3.Distance(_player.CameraAnchor.position, _bottle.transform.position):0.00} m)");
                    Go(Step.Carry);
                    return;

                case Step.Carry:
                    Drive(1f, 0f);
                    if (t < 1.0) return;
                    Drive(0f, 0f);
                    float dist = Vector3.Distance(_bottle.transform.position, _player.transform.position + Vector3.up * 1.1f);
                    Check(carrying && dist < 1.6f, $"carried item follows the hands while walking ({dist:0.00} m from chest)");
                    me.SetCurrent(Attr.Grog, 50f);
                    _player.DebugSetDrunkRagdoll(0f, 0f);
                    _mark = _bottle.Attributes.GetCurrent(Attr.Sips);
                    Go(Step.Drink);
                    return;

                case Step.Drink:
                    if (!_flag)
                    {
                        if (!Act(NetworkInputData.ButtonDrink, t, () => (_player.ActiveStates & PlayerState.DrinkingRum) != 0)) return;
                        _flag = (_player.ActiveStates & PlayerState.DrinkingRum) != 0;
                        if (!Check(_flag, "Q with a bottle → a sip (DrinkingRum)")) { Go(Step.SpawnChest); return; }
                        _stepStart = EditorApplication.timeSinceStartup;
                        return;
                    }
                    if ((_player.ActiveStates & PlayerState.DrinkingRum) != 0 && t < 6.0) return; // wait for the sip to end
                    DrinkData drink = _bottle.Attributes.GetData<DrinkData>();
                    Check(Mathf.Approximately(_bottle.Attributes.GetCurrent(Attr.Sips), _mark - 1f), $"bottle sips {_mark:0} → {_bottle.Attributes.GetCurrent(Attr.Sips):0}");
                    Check(Mathf.Abs(_player.Drunkenness - drink.IntoxicationPerSip) < 0.01f, $"intoxication from the bottle ({_player.Drunkenness:0.00})");
                    Check(Mathf.Abs(me.GetCurrent(Attr.Grog) - (50f + drink.GrogPerSip)) < 0.5f, $"grog restored (+{drink.GrogPerSip:0} → {me.GetCurrent(Attr.Grog):0})");
                    Go(Step.SpawnChest);
                    return;

                case Step.SpawnChest:
                    if (Once())
                    {
                        var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(ItemsSetup.CatalogPath);
                        Vector3 pos = DeckSpot(1.8f, 0.7f, 0.7f, null); // on the deck, never over the water
                        NetworkObject obj = _player.Runner.Spawn(catalog.Get("Chest"), pos, Quaternion.Euler(0f, _player.ViewYaw, 0f));
                        _chest = obj != null ? obj.GetComponent<WorldItem>() : null;
                        if (_chest != null) _chest.Body.linearVelocity = _player.GetComponent<Rigidbody>().linearVelocity;
                    }
                    if (t < 1.0) return;
                    if (_chest == null || !_chest.TryGetComponent(out ItemStorage _))
                    {
                        Finish("test chest spawned", false);
                        return;
                    }
                    Go(Step.Store);
                    return;

                case Step.Store:
                    if (!Act(NetworkInputData.ButtonInteract, t, () => _bottle.IsStored, Ref(_chest))) return;
                    ItemStorage storage = _chest.GetComponent<ItemStorage>();
                    Check(storage.Count == 1 && _bottle.IsStored && !_player.CarriedId.IsValid,
                          $"E on the chest with the bottle → stored ({storage.Count}/{storage.Capacity}), hands free");
                    Check(!_bottle.Colliders[0].enabled, "stored item is hidden (no collisions)");
                    Go(Step.Take);
                    return;

                case Step.Take:
                    if (!Act(NetworkInputData.ButtonInteract, t, () => _player.CarriedId == _bottle.Object.Id, Ref(_chest))) return;
                    Check(_player.CarriedId == _bottle.Object.Id && !_bottle.IsStored && _chest.GetComponent<ItemStorage>().Count == 0,
                          "E on the chest empty-handed → the bottle back in hands");
                    Go(Step.PutDown);
                    return;

                case Step.PutDown:
                    if (!Act(NetworkInputData.ButtonDrop, t, () => !_player.CarriedId.IsValid)) return;
                    Check(!_player.CarriedId.IsValid && !_bottle.IsHeld && _bottle.Body.useGravity, "G → put down (released, gravity back)");
                    Go(Step.PickAgain);
                    return;

                case Step.PickAgain:
                    if (Once()) PlaceInFront(_bottle);
                    if (!Act(NetworkInputData.ButtonInteract, t, () => _player.CarriedId == _bottle.Object.Id, Ref(_bottle))) return;
                    Check(_player.CarriedId == _bottle.Object.Id, "picked up again");
                    Go(Step.Throw);
                    return;

                case Step.Throw:
                    _mark2 = Mathf.Max(_mark2, (_bottle.Body.linearVelocity - _player.GetComponent<Rigidbody>().linearVelocity).magnitude);
                    if (!_flag)
                    {
                        if (!Act(NetworkInputData.ButtonHaul, t, () => !_player.CarriedId.IsValid)) return;
                        _flag = true;
                        _stepStart = EditorApplication.timeSinceStartup;
                        return;
                    }
                    if (t < 0.3) return;
                    Check(!_player.CarriedId.IsValid && _mark2 > 6f, $"LMB → thrown (peak speed relative to the thrower {_mark2:0.0} m/s)");
                    Go(Step.HeavyPick);
                    return;

                case Step.HeavyPick:
                    if (Once()) PlaceInFront(_powder);
                    if (!Act(NetworkInputData.ButtonInteract, t, () => _player.CarriedId == _powder.Object.Id, Ref(_powder))) return;
                    Check(_player.CarriedId == _powder.Object.Id, $"powder barrel ({_powder.Mass:0} kg, capacity {_player.CarryCapacity:0}) lifted [{Diag(_powder)}]");
                    _jumps0 = _player.Stamina.Jumps;
                    _mark = me.GetCurrent(Attr.Stamina);
                    Go(Step.HeavyWalk);
                    return;

                case Step.HeavyWalk:
                    Drive(Mathf.Sin((float)t * Mathf.PI * 2f) >= 0f ? 1f : -1f, 0f);
                    _input.Buttons.Set(NetworkInputData.ButtonJump, ((int)(t * 4.0)) % 2 == 1); // several presses
                    if (t < 2.5) return;
                    Drive(0f, 0f);
                    _input.Buttons.Set(NetworkInputData.ButtonJump, false);
                    float walk = _player.Config != null ? _player.Config.MoveSpeed : 4.5f;
                    float mean = _speedCount > 0 ? _speedSum / _speedCount : 0f;
                    Check(_player.CarriedId == _powder.Object.Id, "still carrying the barrel");
                    Check(mean <= walk * 0.6f, $"full load walks at ~half speed (mean {mean:0.0} m/s ≤ {walk * 0.6f:0.0}; empty-handed walk {walk:0.0})");
                    Check(_player.Stamina.Jumps == _jumps0, "no jump with a full load");
                    Check(me.GetCurrent(Attr.Stamina) < _mark - 1f, $"carrying costs stamina ({_mark:0.0} → {me.GetCurrent(Attr.Stamina):0.0})");
                    Go(Step.HeavyDrop);
                    return;

                case Step.HeavyDrop:
                    if (!Act(NetworkInputData.ButtonDrop, t, () => !_player.CarriedId.IsValid)) return;
                    Check(!_player.CarriedId.IsValid, "heavy barrel put down");
                    _powder.Attributes.Set.SetBase(Attr.Mass, 100f);
                    Go(Step.TooHeavy);
                    return;

                case Step.TooHeavy:
                    if (Once()) PlaceInFront(_powder);
                    if (!Act(NetworkInputData.ButtonInteract, t, () => _player.CarriedId.IsValid, Ref(_powder), timeout: 1.0)) return;
                    var pick = _powder.GetComponent<ItemInteractable>();
                    var who = new InteractorInfo(_player.Object.InputAuthority, Vector3.zero, Vector3.zero, -1, null, default, _player.CarryCapacity);
                    Check(!_player.CarriedId.IsValid && !pick.IsAvailable(who), $"100 kg > capacity: not picked up (prompt '{pick.GetPrompt(who)}')");
                    int slot = _powder.Attributes.Set.SlotOf(Attr.Mass);
                    _powder.Attributes.Set.SetSlotBase(slot, _powder.Attributes.Set.Layout.BaseValues[slot]);
                    // Out of the way: the next item is placed on this very spot.
                    _powder.Teleport(_player.transform.position + Quaternion.Euler(0f, _player.ViewYaw, 0f) * Vector3.right * 2.2f + Vector3.up * 0.6f);
                    _powder.Body.linearVelocity = _player.GetComponent<Rigidbody>().linearVelocity;
                    Go(Step.CompassPick);
                    return;

                case Step.CompassPick:
                    if (Once()) PlaceInFront(_compass);
                    if (!Act(NetworkInputData.ButtonInteract, t, () => _player.CarriedId == _compass.Object.Id, Ref(_compass))) return;
                    Check(_player.CarriedId == _compass.Object.Id, $"compass in hands [{Diag(_compass)}]");
                    Go(Step.CompassStore);
                    return;

                case Step.CompassStore:
                    if (!Act(NetworkInputData.ButtonInteract, t, () => _compass.IsStored, Ref(_chest))) return;
                    Check(_compass.IsStored, "compass stored in the chest");
                    _chest.Attributes.Set.SetCurrent(Attr.Integrity, 0f);
                    Go(Step.BreakChest);
                    return;

                case Step.BreakChest:
                    if (t < 1.5) return;
                    bool chestGone = _chest == null || _chest.Object == null || !_chest.Object.IsValid;
                    Check(chestGone, "chest at 0 integrity → broken and despawned");
                    Check(_compass != null && _compass.Object.IsValid && !_compass.IsStored && _compass.Colliders[0].enabled,
                          "its contents spilled out (compass free and solid again)");
                    // Drop the crate hard onto free deck next to the crew member.
                    _mark = _crate.Attributes.GetCurrent(Attr.Integrity);
                    _crate.Teleport(DeckSpot(1.6f, 3f, 0.5f, _crate));
                    _crate.Body.linearVelocity = _player.GetComponent<Rigidbody>().linearVelocity + Vector3.down * 16f;
                    _crate.Body.angularVelocity = Vector3.zero;
                    Go(Step.Impact);
                    return;

                case Step.Impact:
                    _mark2 = Mathf.Max(_mark2, _crate.Body.linearVelocity.magnitude);
                    if (t < 1.0) return;
                    float hp = _crate.Attributes.GetCurrent(Attr.Integrity);
                    Check(hp < _mark - 5f, $"hard impact damages the crate ({_mark:0} → {hp:0}; peak fall speed {_mark2:0.0}; [{Diag(_crate)}])");
                    Go(Step.Float);
                    return;

                case Step.Float:
                    if (t < 0.2) return;
                    var ocean = Object.FindAnyObjectByType<OceanWaveField>();
                    if (ocean != null && _floatBarrel != null)
                    {
                        Vector3 p = _floatBarrel.transform.position;
                        float surface = ocean.SampleHeight(p);
                        Check(p.y > surface - 0.8f && p.y < surface + 0.8f && Mathf.Abs(_floatBarrel.Body.linearVelocity.y) < 2f,
                              $"barrel in the water floats at the surface (y {p.y:0.00}, surface {surface:0.00})");
                    }
                    else Check(false, "ocean + water barrel present");
                    Go(Step.NoBottle);
                    return;

                case Step.NoBottle:
                    _flag |= (_player.ActiveStates & PlayerState.DrinkingRum) != 0;
                    if (!Act(NetworkInputData.ButtonDrink, t, () => false, timeout: 1.0)) return;
                    Check(!_player.CarriedId.IsValid && !_flag, "Q empty-handed → no sip (rum only from a bottle)");
                    Finish(null, true);
                    return;
            }
        }

        // ───────────────────────── helpers ─────────────────────────

        private static Vector3 Forward() => Quaternion.Euler(0f, _player.ViewYaw, 0f) * Vector3.forward;

        private static string Diag(WorldItem i)
        {
            if (i == null) return "null";
            if (i.Object == null || !i.Object.IsValid) return "despawned";
            var who = new InteractorInfo(_player.Object.InputAuthority, Vector3.zero, Vector3.zero, -1, null, _player.CarriedId, _player.CarryCapacity);
            var pick = i.GetComponent<ItemInteractable>();
            return $"broken {(bool)i.Broken}, hp {i.Attributes.GetCurrent(Attr.Integrity):0}, stored {i.IsStored}, holder {i.Holder}, " +
                   $"eye→item {Vector3.Distance(_player.CameraAnchor.position, i.transform.position):0.00} m, available {(pick != null && pick.IsAvailable(who))}, " +
                   $"last hit {i.LastImpactSpeed:0.0} m/s (pre {i.LastImpactPreStep:0.0}) with {i.LastImpactWith}";
        }

        // The ship drifts: an item placed with zero velocity would be left behind by the deck.
        // After walking around, "1 m ahead" may be inside a mast or a deckhouse: teleporting an item into
        // geometry makes the solver fling it away. Try directions around the crew member for free space.
        private static void PlaceInFront(WorldItem item)
        {
            if (item == null) return;
            float radius = Mathf.Max(item.WorldBounds.extents.magnitude, 0.1f) + 0.05f;
            item.Teleport(DeckSpot(0.6f + radius, 1.0f, radius, item));
            item.Body.linearVelocity = _player.GetComponent<Rigidbody>().linearVelocity;
            item.Body.angularVelocity = Vector3.zero;
        }

        /// <summary>Spawn a fresh item from the catalog in free space ~2.5 m around the crew member.</summary>
        private static WorldItem SpawnTestItem(string id)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(ItemsSetup.CatalogPath);
            GameObject prefab = catalog != null ? catalog.Get(id) : null;
            if (prefab == null) return null;
            Vector3 pos = DeckSpot(2.5f, 1.2f, 0.7f, null);
            NetworkObject obj = _player.Runner.Spawn(prefab, pos, Quaternion.identity);
            return obj != null ? obj.GetComponent<WorldItem>() : null;
        }

        private static RigidbodyConstraints _shipConstraints;

        // Freeze / release the brig so the deck doesn't drift or roll under the test. Constraints, not
        // isKinematic: a kinematic hull stopped colliding with the items (they fell through the deck).
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
                else
                {
                    ship.Body.constraints = _shipConstraints;
                }
            }
        }

        private static void Trace(string line) => Report.AppendLine($"  .. {line}");

        private static string NameOf(NetworkId id)
        {
            if (!id.IsValid) return "nothing";
            foreach (WorldItem i in WorldItem.All)
                if (i != null && i.Object != null && i.Object.IsValid && i.Object.Id == id) return i.DisplayName;
            return id.ToString();
        }

        private static readonly Collider[] Overlaps = new Collider[32];

        /// <summary>
        /// A point <paramref name="distance"/> from the crew member, <paramref name="height"/> above their feet,
        /// with free space of <paramref name="radius"/> and the ship's deck below it — free space alone could be
        /// over the water past the rail. Tries directions around the crew member, view direction first.
        /// </summary>
        private static Vector3 DeckSpot(float distance, float height, float radius, WorldItem ignore)
        {
            Vector3 fallback = _player.transform.position + Forward() * distance + Vector3.up * height;
            for (int a = 0; a < 16; a++)
            {
                float angle = (a % 2 == 0 ? 1f : -1f) * ((a + 1) / 2) * 22.5f;
                Vector3 dir = Quaternion.Euler(0f, _player.ViewYaw + angle, 0f) * Vector3.forward;
                Vector3 candidate = _player.transform.position + dir * distance + Vector3.up * height;
                if (IsFree(candidate, radius, ignore) && OverDeck(candidate, height + 2f, radius, ignore)) return candidate;
            }
            return fallback;
        }

        // The whole column below must be clear down to the deck (an item lying there or a line in the way
        // cushions a drop): sphere-cast with the item's radius, skipping the crew member and the item itself.
        private static bool OverDeck(Vector3 point, float maxDrop, float radius, WorldItem item)
        {
            RaycastHit[] hits = Physics.SphereCastAll(point, Mathf.Max(0.05f, radius * 0.9f), Vector3.down, maxDrop, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.transform.IsChildOf(_player.transform) || (item != null && hit.collider.transform.IsChildOf(item.transform)))
                    continue;
                return hit.collider.GetComponentInParent<NetworkShip>() != null && hit.collider.GetComponentInParent<WorldItem>() == null &&
                       hit.normal.y > 0.7f; // the deck itself, not a line / rail on the way down
            }
            return false;
        }

        private static bool IsFree(Vector3 center, float radius, WorldItem item)
        {
            int n = Physics.OverlapSphereNonAlloc(center, radius, Overlaps, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = Overlaps[i];
                if (c.transform.IsChildOf(_player.transform) || (item != null && c.transform.IsChildOf(item.transform))) continue;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Release the button ≥ 0.3 s, then hold it until <paramref name="done"/> (or the timeout) — robust
        /// to the irregular editor updates of an unfocused editor. True once finished (button released).
        /// </summary>
        private static bool Act(int button, double t, System.Func<bool> done, InteractableRef target = default, double timeout = 3.0)
        {
            _input.Target = target;
            if (t < 0.3)
            {
                _input.Buttons.Set(button, false);
                return false;
            }
            bool finished = done() || t > 0.3 + timeout;
            _input.Buttons.Set(button, !finished);
            return finished;
        }

        private static InteractableRef Ref(WorldItem item)
        {
            if (item == null) return default;
            var i = item.GetComponent<Interactable>();
            return i != null ? i.Ref : default;
        }

        private static void Drive(float forward, float strafe)
        {
            _input.Move = new Vector2(strafe, forward);
            _input.LookYaw = _player.ViewYaw;
        }

        private static WorldItem FindNearest(string profile)
        {
            WorldItem best = null;
            float bestD = float.MaxValue;
            foreach (WorldItem i in WorldItem.All)
            {
                if (i == null || i.Attributes.Profile == null || i.Attributes.Profile.name != profile) continue;
                float d = (i.transform.position - _player.transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        private static WorldItem FindFarthest(string profile)
        {
            WorldItem best = null;
            float bestD = -1f;
            foreach (WorldItem i in WorldItem.All)
            {
                if (i == null || i.Attributes.Profile == null || i.Attributes.Profile.name != profile) continue;
                float d = (i.transform.position - _player.transform.position).sqrMagnitude;
                if (d > bestD) { bestD = d; best = i; }
            }
            return best;
        }

        private static void Go(Step step)
        {
            _step = step;
            _stepStart = EditorApplication.timeSinceStartup;
            _flag = false;
            _entered = false;
            _mark2 = 0f;
            _speedSum = 0f;
            _speedCount = 0;
            if (_player != null)
            {
                _input.Buttons = default;
                _input.Target = default;
            }
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
            string text = $"[ItemsSmoke] {(_failures == 0 ? "PASS" : $"{_failures} FAILED")}\n{Report}";
            if (_failures == 0) Debug.Log(text);
            else Debug.LogError(text);
            EditorApplication.isPlaying = false;
        }
    }
}
#endif
