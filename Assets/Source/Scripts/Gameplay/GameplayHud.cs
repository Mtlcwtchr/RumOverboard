#if FUSION2
using Cysharp.Threading.Tasks;
using Fusion;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RumOverboard.Gameplay
{
    /// <summary>
    /// Minimal in-game overlay: shows the live session name + crew count and a Leave
    /// button. Reads straight off the active NetworkRunner (the one the Fusion Menu
    /// created), so it needs no wiring to the connection layer. Leave shuts the runner
    /// down and returns to the menu.
    ///
    /// If the label/button references are left empty it builds a plain canvas at runtime,
    /// so the gameplay scene works with nothing but this component on an empty object.
    /// Uses legacy uGUI on purpose — no dependency on imported TMP essentials.
    /// </summary>
    public sealed class GameplayHud : MonoBehaviour
    {
        [SerializeField] private Text sessionLabel;
        [SerializeField] private Text crewLabel;
        [SerializeField] private Button leaveButton;

        [Tooltip("Scene to return to when leaving the session.")]
        [SerializeField] private string menuScene = "FusionSampleMenu";

        private bool _leaving;

        private void Awake()
        {
            if (sessionLabel == null || crewLabel == null || leaveButton == null)
                BuildUi();

            if (leaveButton != null)
                leaveButton.onClick.AddListener(() => Leave().Forget());
        }

        private void Update()
        {
            NetworkRunner runner = ActiveRunner();
            if (runner == null || !runner.IsRunning)
            {
                if (sessionLabel != null) sessionLabel.text = "Not connected";
                if (crewLabel != null) crewLabel.text = string.Empty;
                return;
            }

            if (sessionLabel != null)
                sessionLabel.text = $"Session: {runner.SessionInfo.Name}  ({runner.GameMode})";

            if (crewLabel != null)
            {
                int crew = runner.SessionInfo.PlayerCount;
                int max = runner.SessionInfo.MaxPlayers;
                int ping = (int)(runner.GetPlayerRtt(runner.LocalPlayer) * 1000);
                crewLabel.text = $"Crew: {crew}/{max}    Ping: {ping} ms";
            }
        }

        private async UniTask Leave()
        {
            if (_leaving) return;
            _leaving = true;

            NetworkRunner runner = ActiveRunner();
            if (runner != null && runner.IsRunning)
                await runner.Shutdown();

            SceneManager.LoadScene(menuScene, LoadSceneMode.Single);
        }

        private static NetworkRunner ActiveRunner()
        {
            foreach (var r in NetworkRunner.Instances)
                if (r != null && r.IsRunning)
                    return r;
            return null;
        }

        // ---- Runtime-built fallback UI ------------------------------------------
        private void BuildUi()
        {
            EnsureEventSystem();

            var canvasGo = new GameObject("HUD Canvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                        ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

            if (sessionLabel == null)
                sessionLabel = MakeLabel(canvas.transform, font, "Session:",
                    new Vector2(0f, 1f), new Vector2(20f, -20f), TextAnchor.UpperLeft);

            if (crewLabel == null)
                crewLabel = MakeLabel(canvas.transform, font, "Crew:",
                    new Vector2(0f, 1f), new Vector2(20f, -50f), TextAnchor.UpperLeft);

            if (leaveButton == null)
                leaveButton = MakeButton(canvas.transform, font, "Leave",
                    new Vector2(1f, 1f), new Vector2(-20f, -20f));
        }

        private static Text MakeLabel(Transform parent, Font font, string text,
            Vector2 anchor, Vector2 offset, TextAnchor align)
        {
            var go = new GameObject("Label", typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = 24;
            t.color = Color.white;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;

            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = new Vector2(600f, 30f);
            rt.anchoredPosition = offset;
            return t;
        }

        private static Button MakeButton(Transform parent, Font font, string label,
            Vector2 anchor, Vector2 offset)
        {
            var go = new GameObject("Leave Button", typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.7f, 0.15f, 0.15f, 0.9f);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = new Vector2(160f, 44f);
            rt.anchoredPosition = offset;

            var txt = MakeLabel(go.transform, font, label, new Vector2(0.5f, 0.5f),
                Vector2.zero, TextAnchor.MiddleCenter);
            txt.rectTransform.anchorMin = Vector2.zero;
            txt.rectTransform.anchorMax = Vector2.one;
            txt.rectTransform.sizeDelta = Vector2.zero;

            return go.GetComponent<Button>();
        }

        private static void EnsureEventSystem()
        {
            // Project uses the new Input System (activeInputHandler = 1), so the UI needs
            // InputSystemUIInputModule, not the legacy StandaloneInputModule. Normally the
            // Fusion Menu's own EventSystem is already loaded underneath, so this is only
            // hit when the gameplay scene is run on its own.
            if (FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }
    }
}
#endif
