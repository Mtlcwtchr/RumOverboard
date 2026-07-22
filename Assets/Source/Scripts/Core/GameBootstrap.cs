using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RumOverboard.Core
{
    /// <summary>
    /// The Boot scene's only job: get the game into a known state and hand off to the
    /// menu. Kept as the single entry point so any one-time init (quality, app settings,
    /// services) has a home before the Fusion Menu owns the flow. From the menu on, the
    /// Fusion Menu's connection behaviour loads the gameplay scene additively.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Tooltip("Scene to load after boot — the Fusion Menu.")]
        [SerializeField] private string menuScene = "FusionSampleMenu";

        [Tooltip("Small delay so a splash/logo frame can render before the menu loads.")]
        [SerializeField] private float bootDelaySeconds = 0f;

        private async void Start()
        {
            if (bootDelaySeconds > 0f)
                await UniTask.Delay((int)(bootDelaySeconds * 1000));

            SceneManager.LoadScene(menuScene, LoadSceneMode.Single);
        }
    }
}
