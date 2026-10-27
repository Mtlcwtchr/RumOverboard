#if FUSION2
using System;
using System.Collections;
using System.IO;
using System.Text;
using Fusion;
using RumOverboard.Gameplay.Attributes;
using RumOverboard.Gameplay.Crew;
using RumOverboard.Gameplay.Fire;
using RumOverboard.Gameplay.Items;
using RumOverboard.Networking;
using RumOverboard.StateMachine;
using UnityEngine;

namespace RumOverboard.Gameplay.Playtest
{
    /// <summary>
    /// Automated two-instance check of a playtest build (inactive unless started with <c>-probe &lt;dir&gt;</c>):
    ///   RumOverboard.exe -host   -session X -probe DIR      (first)
    ///   RumOverboard.exe -client -session X -probe DIR      (second)
    /// Host: gives its crew member a bottle, drains stamina, gets drunk, sets the fire pile alight, puts it out,
    /// blows up the powder barrel. Client: walks forward (its input → host → back) and checks that it sees all
    /// of it. Each writes DIR/host.txt | DIR/client.txt (PASS/FAIL lines) and screenshots, then quits.
    /// </summary>
    public sealed class PlaytestNetProbe : MonoBehaviour
    {
        private string _dir;
        private string _role = "?";
        private readonly StringBuilder _report = new();
        private int _failures;
        private ConnectionManager _cm;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string dir = PlaytestBoot.ArgValue("-probe");
            if (dir == null || FindAnyObjectByType<PlaytestNetProbe>() != null) return;
            var go = new GameObject("PlaytestNetProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<PlaytestNetProbe>()._dir = dir;
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(_dir);
            float hardStop = Time.realtimeSinceStartup + 240f;
            StartCoroutine(Watchdog(hardStop));

            NetworkRunner runner = null;
            yield return Until(() => (runner = Runner()) != null, 120f);
            if (!Check(runner != null, "a running session within 120 s")) { Finish(); yield break; }
            _role = runner.GameMode == GameMode.Single ? "offline" : runner.IsServer ? "host" : $"client{runner.LocalPlayer.PlayerId}";
            Log($"role {_role}, session '{runner.SessionInfo?.Name}', region {runner.SessionInfo?.Region}, mode {runner.GameMode}");
            if (!Check(runner.GameMode != GameMode.Single, "online (not the offline fallback)")) { Finish(); yield break; }

            int need = int.TryParse(PlaytestBoot.ArgValue("-probeplayers"), out int n) ? Mathf.Max(2, n) : 2;
            yield return Until(() => Count(runner.ActivePlayers) >= need && LiveCrew() >= need, 150f);
            Check(Count(runner.ActivePlayers) >= need && LiveCrew() >= need, $"{need} players in the session, every crew member spawned here ({LiveCrew()})");
            yield return Until(() => NetworkShip.All.Count > 0 && WorldItem.All.Count >= 12, 30f);
            Check(NetworkShip.All.Count > 0, "ship spawned here");
            Check(WorldItem.All.Count >= 12, $"scene items here ({WorldItem.All.Count})");
            Check(FireVisuals.Instance != null, "fire visuals in the scene");
            yield return new WaitForSecondsRealtime(3f); // both sides settle (the other one may be a second behind)
            Shot("01_start");

            if (runner.IsServer) yield return Host(runner);
            else yield return Client(runner);
            Finish();
        }

        // ───────────────────────── host ─────────────────────────

        private IEnumerator Host(NetworkRunner runner)
        {
            NetworkPlayer me = Crew(local: true), other = Crew(local: false);
            if (!Check(me != null && other != null, "host: both crew members")) yield break;
            FireDirector fire = FireDirector.Instance;
            Check(fire != null && fire.Runner == runner, "host: fire director runs on this runner");

            WorldItem bottle = Nearest("GrogBottle", me.transform.position);
            if (Check(bottle != null, "host: a bottle on deck")) me.DebugGiveItem(bottle);
            AttributeSet set = me.Attributes.Set;
            int regen = set.SlotOf(Attr.StaminaRegen);
            if (regen >= 0) set.SetSlotBase(regen, 0f); // hold it low until the client has seen it
            set.SetCurrent(Attr.Stamina, 15f);
            CrewIntoxication.Set(set, 0.5f);
            Vector3 otherStart = OnShip(other);
            Log("host: bottle in hands, stamina 15 (regen held), drunkenness 0.5; waiting for the client to walk");

            yield return new WaitForSecondsRealtime(7f);
            float walked = Vector3.Distance(OnShip(other), otherStart);
            Check(walked > 0.8f, $"host: the client's crew member walked on its input ({walked:0.0} m on deck)");
            if (regen >= 0) set.SetSlotBase(regen, set.Layout.BaseValues[regen]);

            WorldItem crate = Nearest("Crate", Bow(me));
            Check(crate != null && fire != null && fire.Ignite(crate), "host: fire pile (crate) set alight");
            yield return new WaitForSecondsRealtime(6f);
            Shot("02_fire");
            int burning = 0;
            foreach (WorldItem i in WorldItem.All)
                if (Live(i) && i.Attributes.HasFlag(AttrFlag.Burning)) { burning++; fire.Extinguish(i, soak: true); }
            Check(burning >= 1, $"host: put out {burning} burning item(s)");

            // A fresh powder barrel at the bow (the scene's one slides around the rolling deck and may have
            // caught fire from the pile / been soaked by now).
            var spawner = FindAnyObjectByType<ItemSpawner>();
            GameObject prefab = spawner != null && spawner.Catalog != null ? spawner.Catalog.Get("PowderBarrel") : null;
            NetworkShip ship = NetworkShip.All.Count > 0 ? NetworkShip.All[0] : null;
            WorldItem powder = null;
            if (prefab != null && ship != null)
            {
                NetworkObject obj = runner.Spawn(prefab, ship.transform.TransformPoint(new Vector3(0f, 2.2f, 6f)), ship.transform.rotation);
                powder = obj != null ? obj.GetComponent<WorldItem>() : null;
            }
            yield return new WaitForSecondsRealtime(3f);
            Check(powder != null && fire.Ignite(powder),
                  $"host: powder barrel lit ({(powder == null ? "not spawned" : $"burning {powder.Attributes.HasFlag(AttrFlag.Burning)}, wet {powder.Attributes.HasFlag(AttrFlag.Wet)}, broken {(bool)powder.Broken}")})");
            yield return new WaitForSecondsRealtime(4f);
            Check(fire.Explosions >= 1, $"host: powder exploded ({fire.Explosions})");
            Shot("03_blast");
            yield return new WaitForSecondsRealtime(6f); // let the client finish its checks
        }

        // ───────────────────────── client ─────────────────────────

        private IEnumerator Client(NetworkRunner runner)
        {
            NetworkPlayer me = Crew(local: true), host = Crew(local: false);
            if (!Check(me != null && host != null, "client: both crew members")) yield break;
            FireVisuals fx = FireVisuals.Instance;

            // Walk forward 1.5 s: input → host simulation → replicated position.
            Vector3 start = OnShip(me);
            ConnectionManager.InputInjector = d => { d.Move = new Vector2(0f, 1f); return d; };
            yield return new WaitForSecondsRealtime(1.5f);
            ConnectionManager.InputInjector = null;
            yield return new WaitForSecondsRealtime(0.5f);
            float walked = Vector3.Distance(OnShip(me), start);
            Check(walked > 0.8f, $"client: own crew member walks (host-simulated, {walked:0.0} m on deck)");

            float minStamina = 1f;
            float end = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < end && !(host.CarriedId.IsValid && host.Drunkenness > 0.45f && minStamina < 0.4f))
            {
                // With more than two players the host's crew member is the one the host made drunk (not just "not me").
                foreach (NetworkPlayer p in NetworkPlayer.All)
                    if (p != null && p.Object != null && p.Object.IsValid && !p.HasInputAuthority && p.Drunkenness > 0.45f && p != host)
                    {
                        host = p;
                        minStamina = 1f;
                    }
                minStamina = Mathf.Min(minStamina, host.Stamina01);
                yield return null;
            }
            Check(host.CarriedId.IsValid && (host.ActiveStates & PlayerState.Carrying) != 0, "client: host crew carries the bottle");
            Check(minStamina < 0.4f, $"client: host crew stamina drop replicated (min {minStamina:P0})");
            Check(Mathf.Abs(host.Drunkenness - 0.5f) < 0.02f, $"client: host crew drunkenness replicated ({host.Drunkenness:0.00})");

            WorldItem crate = null;
            end = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < end && (crate == null || fx.FlamesShown == 0))
            {
                crate = AnyBurning();
                yield return null;
            }
            Check(crate != null && fx.FlamesShown > 0, $"client: fire on the host shows here (flag + {fx.FlamesShown} flame(s))");
            yield return new WaitForSecondsRealtime(1.5f);
            Shot("02_fire");

            // This crate only: the powder lit later burns (and its blast may set the pile alight again).
            int steam = fx.SteamShown;
            end = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < end && Live(crate) && crate.Attributes.HasFlag(AttrFlag.Burning)) yield return null;
            yield return null;
            Check(!Live(crate) || (!crate.Attributes.HasFlag(AttrFlag.Burning) && crate.Attributes.HasFlag(AttrFlag.Wet)),
                  "client: put out on the host → the crate stops burning here and is wet");
            Check(fx.SteamShown > steam, $"client: steam shown here ({fx.SteamShown - steam})");

            end = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < end && fx.ExplosionsShown == 0) yield return null;
            Check(fx.ExplosionsShown >= 1, $"client: powder explosion shown here ({fx.ExplosionsShown})");
            yield return new WaitForSecondsRealtime(0.4f);
            Shot("03_blast");
        }

        // ───────────────────────── helpers ─────────────────────────

        private static NetworkRunner Runner()
        {
            foreach (NetworkRunner r in NetworkRunner.Instances)
                if (r != null && r.IsRunning) return r;
            return null;
        }

        private static int Count(System.Collections.Generic.IEnumerable<PlayerRef> players)
        {
            int n = 0;
            foreach (PlayerRef _ in players) n++;
            return n;
        }

        private static int LiveCrew()
        {
            int n = 0;
            foreach (NetworkPlayer p in NetworkPlayer.All)
                if (p != null && p.Object != null && p.Object.IsValid) n++;
            return n;
        }

        private static NetworkPlayer Crew(bool local)
        {
            foreach (NetworkPlayer p in NetworkPlayer.All)
                if (p != null && p.Object != null && p.Object.IsValid && p.HasInputAuthority == local) return p;
            return null;
        }

        private static bool Live(WorldItem i) => i != null && i.Object != null && i.Object.IsValid && !i.Broken;

        private static WorldItem AnyBurning()
        {
            foreach (WorldItem i in WorldItem.All)
                if (Live(i) && i.Attributes.HasFlag(AttrFlag.Burning)) return i;
            return null;
        }

        /// <summary>Position in ship space (the brig sails on: world distances include its own motion).</summary>
        private static Vector3 OnShip(NetworkPlayer p)
        {
            NetworkShip ship = NetworkShip.All.Count > 0 ? NetworkShip.All[0] : null;
            return ship != null ? ship.transform.InverseTransformPoint(p.transform.position) : p.transform.position;
        }

        private static Vector3 Bow(NetworkPlayer me)
        {
            NetworkShip ship = NetworkShip.All.Count > 0 ? NetworkShip.All[0] : null;
            return ship != null ? ship.transform.position + ship.transform.forward * 6f : me.transform.position;
        }

        private static WorldItem Nearest(string profile, Vector3 to)
        {
            WorldItem best = null;
            float bestD = float.MaxValue;
            foreach (WorldItem i in WorldItem.All)
            {
                if (!Live(i) || i.IsStored || i.Attributes.Profile == null || i.Attributes.Profile.name != profile) continue;
                float d = (i.transform.position - to).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        private static IEnumerator Until(Func<bool> condition, float timeout)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
        }

        private IEnumerator Watchdog(float stopAt)
        {
            while (Time.realtimeSinceStartup < stopAt) yield return null;
            Check(false, "probe finished within 240 s");
            Finish();
        }

        private void Shot(string name)
        {
            // What the look-at prompt shows right now (the prompt UI is built at runtime: read it back).
            var rig = PlayerCameraRig.Instance;
            var hints = RumOverboard.Gameplay.UI.HintOverlay.Instance;
            string prompt = "";
            if (hints != null)
            {
                var f = typeof(RumOverboard.Gameplay.UI.HintOverlay).GetField("_promptText",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var k = typeof(RumOverboard.Gameplay.UI.HintOverlay).GetField("_promptKey",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var t = f?.GetValue(hints) as UnityEngine.UI.Text;
                var kt = k?.GetValue(hints) as UnityEngine.UI.Text;
                if (t != null)
                    prompt = $"prompt active {t.gameObject.activeInHierarchy}, key '{kt?.text}', text '{t.text}', color {t.color}, font {(t.font != null ? t.font.name : "null")}";
            }
            Log($"{name}: focus {(rig != null && rig.Focus != null ? rig.Focus.GetType().Name + " " + rig.Focus.name : "none")}; {prompt}");
            try { ScreenCapture.CaptureScreenshot(Path.Combine(_dir, $"{_role}_{name}.png")); }
            catch (Exception e) { Log($"screenshot failed: {e.Message}"); }
        }

        private bool Check(bool ok, string what)
        {
            _report.AppendLine($"  {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) _failures++;
            return ok;
        }

        private void Log(string line) => _report.AppendLine("  .. " + line);

        private bool _finished;

        private void Finish()
        {
            if (_finished) return;
            _finished = true;
            string text = $"[PlaytestProbe:{_role}] {(_failures == 0 ? "PASS" : $"{_failures} FAILED")}\n{_report}";
            Debug.Log(text);
            try { File.WriteAllText(Path.Combine(_dir, (_role == "?" ? "unknown" : _role) + ".txt"), text); }
            catch (Exception e) { Debug.LogError($"[PlaytestProbe] report: {e.Message}"); }
            StartCoroutine(QuitSoon());
        }

        private static IEnumerator QuitSoon()
        {
            yield return new WaitForSecondsRealtime(2f); // the last screenshot is written at the end of a frame
            Application.Quit();
        }
    }
}
#endif
