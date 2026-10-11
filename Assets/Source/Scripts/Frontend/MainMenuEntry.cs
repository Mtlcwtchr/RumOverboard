#if FUSION2
using RumOverboard.Core;
using UnityEngine;

namespace RumOverboard.Frontend
{
    /// <summary>MainMenu scene entry: makes sure the persistent <see cref="GameSession"/> (+ UI) exists.</summary>
    public sealed class MainMenuEntry : MonoBehaviour
    {
        [SerializeField] private GameConfig config;

        private void Awake()
        {
            GameSession.Ensure(config);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
#endif
