#if FUSION2
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using Fusion;
using RumOverboard.Core.Sim;
using UnityEngine;

namespace RumOverboard.Gameplay.Attributes
{
    /// <summary>
    /// Two-peer replication scenario for AttributesScene (host = main editor, client = MPPM virtual
    /// player or a second editor/build). Dormant unless a run was requested (see <see cref="RequestPath"/>,
    /// written by RumOverboard ▸ Attributes ▸ Run Network Smoke) or <c>runAlways</c> is set.
    ///   host  : once a client joined — Board base Mass = 7 and Floating off, Crate gets BurningFx ×2.
    ///   client: checks it sees exactly that, plus the zone effects (Cold on Player, Wet on barrels),
    ///           that it has no authority (its ApplyEffect is rejected) and that BurningFx expires.
    /// Each role logs [AttributesNetSmoke:Host|Client] and writes a report next to the request file.
    /// </summary>
    public sealed class AttributesNetSmoke : MonoBehaviour
    {
        public static string Folder => Path.Combine(Path.GetTempPath(), "RumOverboard");
        public static string RequestPath => Path.Combine(Folder, "attributes_netsmoke.request");
        /// <summary>One report per peer process: "main" (main editor) or "clone" (MPPM virtual player / build).</summary>
        public static string ReportPath(string peer) => Path.Combine(Folder, $"attributes_netsmoke.{peer}.txt");

        public static string CurrentPeer => Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor ? "main" : "clone";

        private const float RequestMaxAgeSeconds = 600f;

        [SerializeField] private bool runAlways;
        [SerializeField] private float clientJoinTimeout = 240f;
        [Tooltip("Item prefabs for the carry / storage / break replication checks (skipped without it).")]
        [SerializeField] private RumOverboard.Gameplay.Items.ItemCatalog itemCatalog;

        private bool _itemsDone;

        private readonly StringBuilder _report = new();
        private readonly List<string> _attributeErrors = new();
        private int _failures;
        private string _runId = "manual";
        private string _role = "?";
        private bool _waitOk;
        private bool _active;

        // Awake runs before ConnectionManager.Start, so the session override lands before auto-start:
        // a private session per run, never somebody else's "rum-overboard".
        // Roles are fixed (main editor hosts, clone joins): two peers starting AutoHostOrClient at the same
        // moment can both create the session.
        private void Awake()
        {
            _active = TryReadRequest() || runAlways;
            if (!_active || _runId == "manual") return;
            _cm = FindAnyObjectByType<RumOverboard.Networking.ConnectionManager>();
            _session = $"attr-smoke-{_runId}";
            if (_cm != null) _cm.OverrideAutoStart(CurrentPeer == "main" ? GameMode.Host : GameMode.Client, _session);
        }

        private RumOverboard.Networking.ConnectionManager _cm;
        private string _session;

        private IEnumerator Start()
        {
            if (!_active)
                yield break;

            Application.runInBackground = true;
            Application.logMessageReceived += CaptureErrors;
            EnsureFire();

            NetworkRunner runner = null;
            float deadline = Time.realtimeSinceStartup + 90f;
            float nextRetry = Time.realtimeSinceStartup + 3f;
            while (runner == null && Time.realtimeSinceStartup < deadline)
            {
                foreach (NetworkRunner r in NetworkRunner.Instances)
                    if (r != null && r.IsRunning) runner = r;
                // Client mode fails if it asks before the host created the session: retry.
                if (runner == null && CurrentPeer == "clone" && _cm != null && _cm.Runner == null &&
                    _session != null && Time.realtimeSinceStartup > nextRetry)
                {
                    nextRetry = Time.realtimeSinceStartup + 3f;
                    Log("client: session not up yet — retrying join");
                    _cm.StartClient(_session).Forget();
                }
                yield return null;
            }

            if (runner == null)
            {
                _role = "unknown";
                int runners = 0;
                foreach (NetworkRunner r in NetworkRunner.Instances) runners++;
                Check(false, $"a running NetworkRunner appeared within 60 s (runners: {runners}, ConnectionManager: " +
                             $"{FindAnyObjectByType<RumOverboard.Networking.ConnectionManager>() != null}; see this peer's Editor.log)");
                Finish();
                yield break;
            }

            if (runner.GameMode == GameMode.Single)
            {
                _role = "single";
                Check(false, "session is online (got offline Single fallback — no Photon connection?)");
                Finish();
                yield break;
            }

            if (runner.IsServer) yield return Host(runner);
            else yield return Client(runner);
            Finish();
        }

        // ───────────────────────── host ─────────────────────────

        private IEnumerator Host(NetworkRunner runner)
        {
            _role = "host";
            Log($"run {_runId}: hosting session '{runner.SessionInfo.Name}', waiting for a client…");

            yield return WaitFor(() => Count(runner.ActivePlayers) >= 2, clientJoinTimeout);
            if (!Check(_waitOk, $"a client joined within {clientJoinTimeout:0} s")) yield break;

            yield return WaitFor(AllReady, 10f);
            AttributesComponent board = Find("Board"), crate = Find("Crate (static)");
            if (!Check(board != null && crate != null && board.IsAuthority, "host owns Board and Crate")) yield break;

            board.Set.SetBase(Attr.Mass, 7f);
            board.Set.SetFlag(AttrFlag.Floating, false);
            // Enough integrity to survive the burn, so the client can see the drain actually stop.
            crate.Set.SetBase(Attr.Integrity, 200f);
            crate.Set.SetCurrent(Attr.Integrity, 200f);
            bool applied = crate.ApplyEffect(AttrEffect.BurningFx, 2);
            Check(applied, "host: BurningFx ×2 applied to Crate");

            // Host's own crew member (real NetworkPlayer prefab): hold it exhausted, then let it recover.
            RumOverboard.Networking.NetworkPlayer crew = LocalCrew();
            AttributeSet crewSet = crew != null && crew.Attributes != null ? crew.Attributes.Set : null;
            if (Check(crewSet != null && crewSet.Has(Attr.Stamina), "host crew member has stamina attributes"))
            {
                crewSet.SetBase(Attr.StaminaRegen, 0f);
                crewSet.SetCurrent(Attr.Stamina, 10f);
                crewSet.SetFlag(AttrFlag.Exhausted, true);
                RumOverboard.Gameplay.Crew.CrewIntoxication.Set(crewSet, 0.6f); // rum → also the ragdoll baseline
            }
            Log("host: Board mass=7, Floating off; Crate BurningFx ×2; crew stamina 10 + Exhausted (regen held) — client checks follow");
            StartCoroutine(HostItems(crew));

            yield return new WaitForSecondsRealtime(10f);
            if (crewSet != null)
            {
                int slot = crewSet.SlotOf(Attr.StaminaRegen);
                crewSet.SetSlotBase(slot, crewSet.Layout.BaseValues[slot]);
                Log("host: crew stamina regen restored");
            }

            // Stay a while so the client can observe the burn expiring and the crew recovering.
            yield return new WaitForSecondsRealtime(25f);
        }

        private static RumOverboard.Networking.NetworkPlayer LocalCrew()
        {
            foreach (RumOverboard.Networking.NetworkPlayer p in RumOverboard.Networking.NetworkPlayer.All)
                if (p != null && p.HasInputAuthority) return p;
            return null;
        }

        private static RumOverboard.Networking.NetworkPlayer RemoteCrew()
        {
            foreach (RumOverboard.Networking.NetworkPlayer p in RumOverboard.Networking.NetworkPlayer.All)
                if (p != null && !p.HasInputAuthority) return p;
            return null;
        }

        // ───────────────────────── client ─────────────────────────

        private IEnumerator Client(NetworkRunner runner)
        {
            _role = "client";
            Log($"run {_runId}: client of '{runner.SessionInfo.Name}'");
            Check(runner.IsClient && !runner.IsServer, "runner is a pure client");

            yield return WaitFor(AllReady, 30f);
            if (!Check(_waitOk, "all NetworkAttributes spawned on the client")) yield break;

            bool anyAuthority = false;
            foreach (AttributesComponent a in SimWorld.All<AttributesComponent>())
                anyAuthority |= a.IsAuthority;
            Check(!anyAuthority, "client has no authority over any attributes");

            AttributesComponent player = Find("Player"), barrel = Find("Barrel"), powder = Find("PowderBarrel"),
                                board = Find("Board"), crate = Find("Crate (static)");
            if (!Check(player && barrel && powder && board && crate, "all 5 sandbox entities present")) yield break;

            // Real crew member owned by the host (NetworkPlayer prefab; not simulated on this client).
            RumOverboard.Networking.NetworkPlayer hostCrew = null;
            yield return WaitFor(() => (hostCrew = RemoteCrew()) != null && hostCrew.Attributes != null, 15f);
            bool crewOk = Check(_waitOk && hostCrew.Attributes.Has(Attr.Stamina), "host's crew member spawned here with stamina attributes");
            _itemsDone = false;
            StartCoroutine(ClientItems(hostCrew));
            if (crewOk)
            {
                yield return WaitFor(() => hostCrew.IsExhausted && hostCrew.Stamina01 < 0.15f, 20f);
                Check(_waitOk, $"host crew: Exhausted + low stamina replicated (stamina {hostCrew.Stamina01:P0}, exhausted {hostCrew.IsExhausted})");
                Check(!hostCrew.Attributes.IsAuthority, "host crew: read-only on the client");

                // Intoxication moved from PlayerNetState into the attributes: value + its ragdoll effect.
                float influence = hostCrew.Config != null ? hostCrew.Config.DrunkRagdollInfluence : 0.5f;
                yield return WaitFor(() => Mathf.Abs(hostCrew.Drunkenness - 0.6f) < 0.01f &&
                                           hostCrew.RagdollControl >= 0.6f * influence - 0.01f, 15f);
                Check(_waitOk, $"host crew: drunkenness 0.6 replicated via attributes (got {hostCrew.Drunkenness:0.00}), " +
                               $"ragdoll baseline {hostCrew.RagdollControl:0.00} ≥ {0.6f * influence:0.00}");
            }

            // Host-side scripted changes (applied when we joined).
            yield return WaitFor(() => crate.HasEffect(AttrEffect.BurningFx), 30f);
            if (Check(_waitOk, "Crate: BurningFx replicated"))
            {
                AttributeSet.ActiveEffect burn = default;
                foreach (AttributeSet.ActiveEffect e in crate.Set.Effects)
                    if (e.Definition.Id == AttrEffect.BurningFx) burn = e;
                float left = burn.RemainingSeconds(crate.Tick, crate.TickRate);
                Check(burn.Stacks == 2, $"Crate: BurningFx stacks = 2 (got {burn.Stacks})");
                Check(left > 0f && left <= 8.5f, $"Crate: remaining time derived from end tick ({left:0.0}s)");
                Check(crate.HasFlag(AttrFlag.Burning), "Crate: Burning flag replicated");
                float i1 = crate.GetCurrent(Attr.Integrity);
                yield return new WaitForSecondsRealtime(1f);
                float i2 = crate.GetCurrent(Attr.Integrity);
                Check(i2 < i1, $"Crate: Integrity drains on the host and replicates ({i1:0.0} → {i2:0.0})");
            }

            yield return WaitFor(() => Mathf.Approximately(board.Get(Attr.Mass), 7f) && !board.HasFlag(AttrFlag.Floating), 10f);
            Check(_waitOk, $"Board: host base edit + flag replicated (mass {board.Get(Attr.Mass)}, floating {board.HasFlag(AttrFlag.Floating)})");
            Check(board.HasFlag(AttrFlag.CriticalItem), "Board: untouched flags kept");

            // Zone effects (host-side triggers).
            // ColdFx only lowers the host-only StaminaRegen stat — the effect itself is what replicates.
            yield return WaitFor(() => player.HasEffect(AttrEffect.ColdFx), 10f);
            Check(_waitOk, "Player: ColdFx (zone) replicated");
            yield return WaitFor(() => barrel.HasFlag(AttrFlag.Wet) && Mathf.Approximately(barrel.Get(Attr.Mass), 42f), 10f);
            Check(_waitOk, $"Barrel: Wet flag + mass 42 (got {barrel.Get(Attr.Mass)})");
            Check(Mathf.Approximately(powder.Get(Attr.Mass), 62f), $"PowderBarrel: inherited profile + Wet = 62 (got {powder.Get(Attr.Mass)})");

            // Authority guard.
            Check(!board.ApplyEffect(AttrEffect.WetFx), "client ApplyEffect is rejected");
            Check(!board.HasEffect(AttrEffect.WetFx), "client did not change local state");

            // Expiry.
            yield return WaitFor(() => !crate.HasEffect(AttrEffect.BurningFx) && !crate.HasFlag(AttrFlag.Burning), 15f);
            if (Check(_waitOk, "Crate: BurningFx + Burning flag expired on the client"))
            {
                float i3 = crate.GetCurrent(Attr.Integrity);
                yield return new WaitForSecondsRealtime(1f);
                Check(i3 > 0f && Mathf.Approximately(i3, crate.GetCurrent(Attr.Integrity)),
                      $"Crate: Integrity stopped draining above zero ({i3:0.0}; expected ≈ 200 − 2×5×8 = 120)");
            }

            if (crewOk)
            {
                yield return WaitFor(() => !hostCrew.IsExhausted && hostCrew.Stamina01 > 0.3f, 25f);
                Check(_waitOk, $"host crew: regen on the host → recovery replicated (stamina {hostCrew.Stamina01:P0}, exhausted {hostCrew.IsExhausted})");
            }

            yield return WaitFor(() => _itemsDone, 75f);
            Check(_waitOk, "item replication checks finished");
            Check(_attributeErrors.Count == 0, $"no [Attributes] errors (layout hash etc.): {string.Join(" | ", _attributeErrors)}");
        }

        // ───────────────────────── items (runs alongside the attribute checks) ─────────────────────────

        // Host: a bottle in the crew member's hands → into a chest → the chest breaks and spills it.
        private IEnumerator HostItems(RumOverboard.Networking.NetworkPlayer crew)
        {
            if (itemCatalog == null || crew == null)
            {
                Log("host: items skipped (no item catalog / crew)");
                yield break;
            }
            NetworkRunner runner = crew.Runner;
            Vector3 front = crew.transform.position + crew.transform.forward * 1.6f + Vector3.up * 0.6f;
            NetworkObject chestObj = runner.Spawn(itemCatalog.Get("Chest"), front, crew.transform.rotation);
            NetworkObject bottleObj = runner.Spawn(itemCatalog.Get("GrogBottle"), crew.transform.position + Vector3.up * 1.2f);
            var chest = chestObj != null ? chestObj.GetComponent<RumOverboard.Gameplay.Items.ItemStorage>() : null;
            var bottle = bottleObj != null ? bottleObj.GetComponent<RumOverboard.Gameplay.Items.WorldItem>() : null;
            if (!Check(chest != null && bottle != null, "host: chest + bottle spawned")) yield break;

            crew.DebugGiveItem(bottle);
            Log("host: bottle in the crew member's hands");
            yield return new WaitForSecondsRealtime(4f);
            Check(chest.TryStore(bottle), "host: bottle stored in the chest");
            yield return new WaitForSecondsRealtime(4f);
            chest.Self.Break();
            Log("host: chest broken");

            // Fire: a crate burns, gets put out (soaked); a powder barrel away from everyone explodes.
            yield return new WaitForSecondsRealtime(2f);
            var fire = RumOverboard.Gameplay.Fire.FireDirector.Instance;
            Vector3 side = crew.transform.right;
            var crate = SpawnFixed(runner, "Crate", crew.transform.position + side * 3f + Vector3.up * 0.5f);
            var powder = SpawnFixed(runner, "PowderBarrel", crew.transform.position - side * 7f + Vector3.up * 0.5f);
            if (!Check(fire != null && crate != null && powder != null, "host: fire director + fire test items")) yield break;
            yield return new WaitForSecondsRealtime(1f);
            Check(fire.Ignite(crate), "host: crate set on fire");
            yield return new WaitForSecondsRealtime(4f);
            Check(fire.Extinguish(crate, soak: true), "host: crate put out (soaked)");
            yield return new WaitForSecondsRealtime(2f);
            Check(fire.Ignite(powder), "host: powder barrel lit");
            yield return new WaitForSecondsRealtime(4f);
            Check(fire.Explosions == 1, $"host: powder exploded after its fuse ({fire.Explosions})");
        }

        private RumOverboard.Gameplay.Items.WorldItem SpawnFixed(NetworkRunner runner, string id, Vector3 at)
        {
            GameObject prefab = itemCatalog != null ? itemCatalog.Get(id) : null;
            NetworkObject obj = prefab != null ? runner.Spawn(prefab, at, Quaternion.identity) : null;
            var item = obj != null ? obj.GetComponent<RumOverboard.Gameplay.Items.WorldItem>() : null;
            if (item != null) item.Body.isKinematic = true; // no ground needed
            return item;
        }

        // Client: sees the same three states (own waits — the shared WaitFor isn't re-entrant).
        private IEnumerator ClientItems(RumOverboard.Networking.NetworkPlayer hostCrew)
        {
            RumOverboard.Gameplay.Items.WorldItem bottle = null, chest = null;
            float end = Time.realtimeSinceStartup + 20f;
            while ((bottle == null || chest == null) && Time.realtimeSinceStartup < end)
            {
                foreach (var i in RumOverboard.Gameplay.Items.WorldItem.All)
                {
                    if (i == null || i.Attributes.Profile == null) continue;
                    if (i.Attributes.Profile.name == "GrogBottle") bottle = i;
                    if (i.Attributes.Profile.name == "Chest") chest = i;
                }
                yield return null;
            }
            if (!Check(bottle != null && chest != null, "items: bottle + chest replicated to the client"))
            {
                _itemsDone = true;
                yield break;
            }
            var storage = chest.GetComponent<RumOverboard.Gameplay.Items.ItemStorage>();
            Renderer bottleRenderer = bottle.GetComponentInChildren<Renderer>();

            end = Time.realtimeSinceStartup + 10f;
            while (hostCrew.CarriedId != bottle.Object.Id && Time.realtimeSinceStartup < end) yield return null;
            float dist = Vector3.Distance(bottle.transform.position, hostCrew.transform.position);
            Check(hostCrew.CarriedId == bottle.Object.Id && bottle.Holder == hostCrew.Object.InputAuthority && bottleRenderer.enabled && dist < 2.5f,
                  $"items: host crew carries the bottle (CarriedId, Holder, visible, {dist:0.00} m from the carrier)");
            Check((hostCrew.ActiveStates & RumOverboard.StateMachine.PlayerState.Carrying) != 0, "items: Carrying state replicated");

            end = Time.realtimeSinceStartup + 10f;
            while (!(bottle.IsStored && storage.Count == 1) && Time.realtimeSinceStartup < end) yield return null;
            Check(bottle.IsStored && storage.Count == 1 && !bottleRenderer.enabled && !hostCrew.CarriedId.IsValid,
                  $"items: stored on the host → hidden in the chest here ({storage.Count}/{storage.Capacity}), hands empty");

            end = Time.realtimeSinceStartup + 10f;
            while (chest != null && chest.Object != null && chest.Object.IsValid && Time.realtimeSinceStartup < end) yield return null;
            bool gone = chest == null || chest.Object == null || !chest.Object.IsValid;
            Check(gone, "items: broken chest despawned on the client");
            end = Time.realtimeSinceStartup + 3f;
            while (bottle.IsStored && Time.realtimeSinceStartup < end) yield return null;
            Check(!bottle.IsStored && bottleRenderer.enabled, "items: its content spilled out and is visible again");

            // Fire: only replicated flags drive the client's visuals.
            var fx = RumOverboard.Gameplay.Fire.FireVisuals.Instance;
            RumOverboard.Gameplay.Items.WorldItem crate = null, powder = null;
            end = Time.realtimeSinceStartup + 10f;
            while ((crate == null || powder == null) && Time.realtimeSinceStartup < end)
            {
                foreach (var i in RumOverboard.Gameplay.Items.WorldItem.All)
                {
                    if (i == null || i.Attributes.Profile == null) continue;
                    if (i.Attributes.Profile.name == "Crate") crate = i;
                    if (i.Attributes.Profile.name == "PowderBarrel") powder = i;
                }
                yield return null;
            }
            if (Check(fx != null && crate != null && powder != null, "fire: visuals here + fire test items replicated"))
            {
                end = Time.realtimeSinceStartup + 8f;
                while (!(crate.Attributes.HasFlag(AttrFlag.Burning) && fx.FlamesShown > 0) && Time.realtimeSinceStartup < end) yield return null;
                Check(crate.Attributes.HasFlag(AttrFlag.Burning) && fx.FlamesShown > 0, $"fire: crate burning here (flag + flames, {fx.FlamesShown})");
                int steam = fx.SteamShown;
                end = Time.realtimeSinceStartup + 10f;
                while (crate.Attributes.HasFlag(AttrFlag.Burning) && Time.realtimeSinceStartup < end) yield return null;
                yield return null;
                Check(!crate.Attributes.HasFlag(AttrFlag.Burning) && crate.Attributes.HasFlag(AttrFlag.Wet) && fx.SteamShown > steam,
                      "fire: put out on the host → not burning, wet, steam shown here");
                end = Time.realtimeSinceStartup + 12f;
                while (fx.ExplosionsShown == 0 && Time.realtimeSinceStartup < end) yield return null;
                Check(fx.ExplosionsShown == 1, $"fire: powder explosion shown here ({fx.ExplosionsShown})");
            }
            _itemsDone = true;
        }

        /// <summary>Fire director + visuals for the fire checks (AttributesScene has none; the director only runs on the host).</summary>
        private static void EnsureFire()
        {
            if (RumOverboard.Gameplay.Fire.FireVisuals.Instance != null) return;
            var go = new GameObject("Fire (net smoke)");
            go.AddComponent<RumOverboard.Gameplay.Fire.FireDirector>();
            go.AddComponent<RumOverboard.Gameplay.Fire.FireVisuals>();
        }

        // ───────────────────────── helpers ─────────────────────────

        private bool TryReadRequest()
        {
            try
            {
                if (!File.Exists(RequestPath)) return false;
                if ((DateTime.Now - File.GetLastWriteTime(RequestPath)).TotalSeconds > RequestMaxAgeSeconds) return false;
                _runId = File.ReadAllText(RequestPath).Trim();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool AllReady()
        {
            int count = 0;
            foreach (AttributesComponent a in SimWorld.All<AttributesComponent>())
            {
                var net = a.GetComponent<NetworkAttributes>();
                if (net == null || !net.IsReady) return false;
                count++;
            }
            return count > 0;
        }

        private static AttributesComponent Find(string name)
        {
            foreach (AttributesComponent a in SimWorld.All<AttributesComponent>())
                if (a != null && a.name == name) return a;
            return null;
        }

        private static int Count(IEnumerable<PlayerRef> players)
        {
            int n = 0;
            foreach (PlayerRef _ in players) n++;
            return n;
        }

        private IEnumerator WaitFor(Func<bool> condition, float timeout)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end)
                {
                    _waitOk = false;
                    yield break;
                }
                yield return null;
            }
            _waitOk = true;
        }

        private bool Check(bool ok, string what)
        {
            _report.AppendLine($"  {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) _failures++;
            return ok;
        }

        private void Log(string line) => _report.AppendLine("  .. " + line);

        private void CaptureErrors(string message, string stack, UnityEngine.LogType type)
        {
            if ((type == UnityEngine.LogType.Error || type == UnityEngine.LogType.Exception) && message.StartsWith("[Attributes]"))
                _attributeErrors.Add(message);
        }

        private void Finish()
        {
            Application.logMessageReceived -= CaptureErrors;
            string header = $"[AttributesNetSmoke:{_role}@{CurrentPeer}] run {_runId}: {(_failures == 0 ? "PASS" : $"{_failures} FAILED")}";
            string text = header + "\n" + _report;
            if (_failures == 0) Debug.Log(text);
            else Debug.LogError(text);

            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(ReportPath(CurrentPeer), text);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AttributesNetSmoke] Could not write report: {e.Message}");
            }
        }

        private void OnDestroy() => Application.logMessageReceived -= CaptureErrors;
    }
}
#endif
