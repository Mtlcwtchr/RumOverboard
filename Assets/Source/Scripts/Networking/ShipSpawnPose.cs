using UnityEngine;

namespace RumOverboard.Networking
{
    /// <summary>Scene system that decides where the session's ship spawns (e.g. the start harbor).</summary>
    public interface IShipSpawnPoseProvider
    {
        /// <summary>False while the pose isn't known yet (world still generating).</summary>
        bool TryGetShipSpawnPose(out Vector3 position, out Quaternion rotation);
    }

    /// <summary>
    /// Registry for the active <see cref="IShipSpawnPoseProvider"/>. <see cref="ConnectionManager"/>
    /// waits (bounded) while the provider is pending, then spawns the ship — and the crew on its
    /// deck — at the provided pose instead of the scene's ship spawn point.
    /// </summary>
    public static class ShipSpawnPose
    {
        public static IShipSpawnPoseProvider Provider { get; set; }

        public static bool Pending => Provider != null && !Provider.TryGetShipSpawnPose(out _, out _);

        public static bool TryGet(out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            return Provider != null && Provider.TryGetShipSpawnPose(out position, out rotation);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Provider = null;
    }
}
