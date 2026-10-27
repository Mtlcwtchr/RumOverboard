#if FUSION2
using System;
using System.Collections;
using System.Linq;
using Fusion;
using RumOverboard.Gameplay.Interaction;
using RumOverboard.Networking;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Features.Rowing
{
    /// <summary>Opt-in offline host test in RowingScene, enabled with -rowingSmoke.</summary>
    public sealed class RowingSmokeTest : MonoBehaviour
    {
        private NetworkShip _ship;
        private NetworkPlayer _player;
        private NetworkInputData _input;
        private int _failures;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!Environment.GetCommandLineArgs().Contains("-rowingSmoke")) return;
            FindAnyObjectByType<ConnectionManager>()?.OverrideAutoStart(GameMode.Single);
            new GameObject("RowingSmokeTest").AddComponent<RowingSmokeTest>();
        }

        private IEnumerator Start()
        {
            float deadline = Time.realtimeSinceStartup + 40f;
            while (Time.realtimeSinceStartup < deadline)
            {
                _ship = NetworkShip.All.FirstOrDefault(s => s != null && s.HasStateAuthority);
                _player = NetworkPlayer.All.FirstOrDefault(p => p != null && p.HasInputAuthority);
                if (_ship != null && _player != null) break;
                yield return null;
            }
            if (_ship == null || _player == null) { Check(false, "spawn ship + player"); Finish(); yield break; }
            _player.InputOverride = () => _input;
            yield return Wait(2f);
            CapturePreview();
            Check(_ship.OarCount == 6 && Enumerable.Range(0, 6).All(_ship.HasOar), "six sweeps");
            Check(Mathf.Abs(_ship.Body.mass - 27000f) < 1f, "scaled ship mass");
            Check(_ship.GetComponentsInChildren<Rigidbody>().Length == 1, "one hull body");
            Check(Enumerable.Range(0, 6).All(i => _ship.Oar(i).GetComponentsInChildren<Collider>().All(c => c.isTrigger)), "fixed triggers, no blade colliders");
            var scene = FindAnyObjectByType<RowingSceneController>();
            Check(scene != null, "isolated scene controller");

            // Actual station interaction, occupancy, hold vs taps, then release.
            var oar = _ship.Oar(0);
            var rb = _player.GetComponent<Rigidbody>();
            rb.position = oar.StandPosition + Vector3.up * 0.05f;
            rb.rotation = oar.StandRotation;
            rb.linearVelocity = _ship.Body.GetPointVelocity(rb.position);
            _player.transform.SetPositionAndRotation(rb.position, rb.rotation);
            yield return Wait(0.4f);
            yield return Interact(oar.Ref);
            Check(_player.IsRowing && _ship.GetOar(0).Rower == _player.Object.InputAuthority, "E occupies sweep");
            Check(!_ship.TryTakeOar(0, PlayerRef.FromIndex(30)), "occupied sweep rejects another rower");
            _input.Move = Vector2.up;
            yield return Wait(0.15f);
            int stroke = _ship.GetOar(0).StrokeTick;
            yield return Wait(_ship.RowingSettings.StrokePeriod * 2f);
            Check(stroke > 0 && _ship.GetOar(0).StrokeTick == stroke, "holding W does not repeat strokes");
            Check(Vector3.Distance(_player.transform.position, oar.StandPosition) < 0.35f, "rower stays at stand");
            _ship.ResetOars();
            Check(_ship.GetOar(0).Rower == _player.Object.InputAuthority, "reset preserves rower occupancy");
            _input = default;
            yield return Interact(default);
            Check(!_player.IsRowing && !_ship.GetOar(0).Rower.IsRealPlayer, "E releases sweep");
            yield return Interact(oar.Ref);
            _player.DebugSetDrunkRagdoll(0f, 1f);
            yield return Wait(0.25f);
            Check(!_ship.GetOar(0).Rower.IsRealPlayer, "knockout releases sweep");
            _player.DebugSetDrunkRagdoll(0f, 0f);
            yield return Wait(0.5f);

            // A single rower must move and turn the larger ship with sails furled and no windage.
            scene?.ResetShip();
            yield return Wait(1f);
            Vector3 start = _ship.transform.position;
            Vector3 forward = _ship.transform.forward;
            float heading = _ship.transform.eulerAngles.y;
            _ship.SetOarBot(0, 1);
            yield return Wait(12f);
            float distance = Vector3.Dot(_ship.transform.position - start, forward);
            float turn = Mathf.DeltaAngle(heading, _ship.transform.eulerAngles.y);
            Check(distance > 0.25f, $"single rower moves ship: {distance:F2}m in 12s");
            Check(turn > 0.5f, $"port sweep turns bow to starboard: {turn:F2} degrees");
            Check(_ship.Body.linearVelocity.magnitude < _ship.RowingSettings.MaxRowSpeed + 0.5f, "slow rowing speed");
            _ship.ResetOars();
            Check(Enumerable.Range(0, 6).All(i => _ship.GetOarBot(i) == 0 && _ship.GetOar(i).Sign == 0), "reset stops bots and strokes");

            scene?.ResetShip();
            yield return Wait(1f);
            start = _ship.transform.position;
            forward = _ship.transform.forward;
            _ship.SetOarBot(0, -1);
            _ship.SetOarBot(3, -1);
            yield return Wait(8f);
            float backingDistance = Vector3.Dot(_ship.transform.position - start, forward);
            Check(backingDistance < -0.2f, $"both sides back the ship: {backingDistance:F2}m, velocity={_ship.Body.linearVelocity}");
            _ship.ResetOars();

            // Force comparison at the same stroke age isolates the crew synchronization bonus.
            _ship.SetOarBot(0, 1);
            _ship.SetOarBot(3, 1);
            float syncBonus = _ship.RowingSettings.SyncBonusPerRower;
            _ship.RowingSettings.SyncBonusPerRower = 0f;
            yield return Wait(0.4f);
            int now = _ship.Runner.Tick;
            float baseline = ExpectedForce(0, now);
            _ship.RowingSettings.SyncBonusPerRower = syncBonus;
            RowingSystem.Tick(_ship, _ship.Runner.DeltaTime);
            float synced = Mathf.Abs(_ship.GetOarThrust(0));
            Check(baseline > 0f && synced > baseline * 1.05f, $"synchronized rowers gain thrust: {baseline:F0} -> {synced:F0}N");
            _ship.ResetOars();
            Finish();
        }

        private float ExpectedForce(int i, int now)
        {
            var s = _ship.GetOar(i);
            var cfg = _ship.RowingSettings;
            float ahead = Vector3.Dot(_ship.Body.linearVelocity - _ship.WaterVelocity, _ship.transform.forward);
            return cfg.PeakForce * RowingRhythm.ActivePower(s, now) *
                RowingRhythm.DriveShape(s, now, new RowTiming(cfg, _ship.Runner.DeltaTime)) *
                Mathf.Clamp01(1f - ahead / cfg.MaxRowSpeed);
        }
        private void CapturePreview()
        {
            if (!Environment.GetCommandLineArgs().Contains("-rowingPreview") ||
                SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var cameraGo = new GameObject("RowingPreviewCamera");
            var camera = cameraGo.AddComponent<Camera>();
            camera.transform.position = _ship.transform.TransformPoint(new Vector3(-27f, 18f, -29f));
            camera.transform.LookAt(_ship.transform.TransformPoint(new Vector3(0f, 6f, 0f)));
            camera.fieldOfView = 50f;
            camera.farClipPlane = 500f;
            var rt = new RenderTexture(1600, 1000, 24);
            var texture = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0);
                texture.Apply();
                System.IO.Directory.CreateDirectory("Logs");
                System.IO.File.WriteAllBytes("Logs/rowing-preview.png", texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                Destroy(rt); Destroy(texture); Destroy(cameraGo);
            }
        }
        private IEnumerator Interact(InteractableRef target)
        {
            _input = default;
            _input.Target = target;
            _input.Buttons.Set(NetworkInputData.ButtonInteract, true);
            yield return Wait(0.15f);
            _input = default;
            yield return Wait(0.25f);
        }
        private static IEnumerator Wait(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until) yield return null;
        }
        private void Check(bool condition, string message)
        {
            Debug.Log($"[RowingTest] {(condition ? "PASS" : "FAIL")}: {message}");
            if (!condition) _failures++;
        }
        private void Finish()
        {
            Debug.Log($"[RowingTest] RESULT: {(_failures == 0 ? "ALL PASSED" : _failures + " FAILED")}");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(_failures == 0 ? 0 : 1);
#else
            Application.Quit(_failures == 0 ? 0 : 1);
#endif
        }
    }
}
#endif
