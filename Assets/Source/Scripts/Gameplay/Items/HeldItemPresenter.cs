#if FUSION2
using RumOverboard.Gameplay.Attributes;
using RumOverboard.Networking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RumOverboard.Gameplay.Items
{
    /// <summary>
    /// Local presentation of the item in the local crew member's hands (Tier C — never replicated):
    ///   • a line above the stamina bar: "В руках: Бутылка грога (4 глотка)";
    ///   • spyglass: hold Q to look through it (camera field of view → SpyglassData.ZoomFov);
    ///   • compass: a heading readout at the top of the screen while you hold it.
    /// Sips (bottle) are simulated on the host — Q is sent as the Drink button, this only shows.
    /// </summary>
    public sealed class HeldItemPresenter : MonoBehaviour
    {
        private Camera _camera;
        private float _baseFov = -1f;
        private GUIStyle _style;
        private GUIStyle _compassStyle;

        public static bool UseHeld
        {
            get
            {
                var kb = Keyboard.current;
                var gp = Gamepad.current;
                return (kb != null && kb.qKey.isPressed) || (gp != null && gp.buttonNorth.isPressed);
            }
        }

        private NetworkPlayer Local
        {
            get
            {
                foreach (NetworkPlayer p in NetworkPlayer.All)
                    if (p != null && p.Object != null && p.Object.IsValid && p.HasInputAuthority) return p;
                return null;
            }
        }

        private void LateUpdate()
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;
            if (_baseFov < 0f) _baseFov = _camera.fieldOfView;

            WorldItem item = Local != null ? Local.CarriedItem : null;
            SpyglassData glass = item != null ? item.Attributes.GetData<SpyglassData>() : null;
            float target = glass != null && UseHeld ? glass.ZoomFov : _baseFov;
            float speed = glass != null ? glass.ZoomSpeed : 8f;
            _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, target, 1f - Mathf.Exp(-speed * Time.deltaTime));
        }

        private void OnGUI()
        {
            NetworkPlayer local = Local;
            WorldItem item = local != null ? local.CarriedItem : null;
            if (item == null) return;

            _style ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 16, richText = true };
            _compassStyle ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 22, richText = true };

            string extra = "";
            AttributesComponent a = item.Attributes;
            if (a.GetData<DrinkData>() != null)
            {
                int sips = Mathf.RoundToInt(a.GetCurrent(Attr.Sips));
                extra = sips > 0 ? $"  ({sips} глотк., Q — пить)" : "  (пусто)";
            }
            else if (a.GetData<SpyglassData>() != null) extra = "  (Q — смотреть)";
            GUI.Label(new Rect(Screen.width * 0.5f - 250f, Screen.height - 96f, 500f, 24f),
                      $"В руках: <b>{item.DisplayName}</b>{extra}", _style);

            CompassData compass = a.GetData<CompassData>();
            if (compass != null)
            {
                float wobble = (Mathf.PerlinNoise(Time.time * 0.7f, 3.1f) * 2f - 1f) * compass.NeedleWobble;
                float heading = Mathf.Repeat(local.ViewYaw + wobble, 360f);
                GUI.Box(new Rect(Screen.width * 0.5f - 90f, 20f, 180f, 40f), $"{Cardinal(heading)}  {heading:000}°", _compassStyle);
            }
        }

        private static string Cardinal(float deg)
        {
            string[] names = { "С", "СВ", "В", "ЮВ", "Ю", "ЮЗ", "З", "СЗ" };
            return names[Mathf.RoundToInt(deg / 45f) % 8];
        }
    }
}
#endif
