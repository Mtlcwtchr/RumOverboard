#if FUSION2
using Fusion;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean
{
    /// <summary>
    /// Handles helm occupancy and body pinning while steering.
    /// NetworkPlayer delegates helm-specific gameplay to this class.
    /// </summary>
    public sealed class HelmOccupancyController
    {
        private readonly Rigidbody _body;
        private bool _preSteerKinematic;

        public ShipHelm ActiveHelm { get; private set; }
        public bool IsPinnedToHelm { get; private set; }

        public HelmOccupancyController(Rigidbody body)
        {
            _body = body;
        }

        public void SyncAuthorityOccupancy(
            bool hasStateAuthority,
            bool steering,
            ShipHelm nearbyHelm,
            PlayerRef player,
            float steerInput)
        {
            if (!hasStateAuthority)
                return;

            if (steering)
            {
                if (ActiveHelm == null && nearbyHelm != null && nearbyHelm.TryOccupy(player))
                    ActiveHelm = nearbyHelm;

                ActiveHelm?.SubmitSteer(steerInput);
                return;
            }

            if (ActiveHelm == null)
                return;

            ActiveHelm.Release(player);
            ActiveHelm = null;
        }

        public void ApplyPinning(bool steering, bool hasAnchor, Vector3 anchorPosition, float anchorYaw)
        {
            if (steering && hasAnchor)
            {
                if (!IsPinnedToHelm)
                {
                    IsPinnedToHelm = true;
                    _preSteerKinematic = _body.isKinematic;
                    _body.isKinematic = true;
                }

                _body.position = anchorPosition;
                _body.rotation = Quaternion.Euler(0f, anchorYaw, 0f);
                return;
            }

            if (!IsPinnedToHelm)
                return;

            IsPinnedToHelm = false;
            _body.isKinematic = _preSteerKinematic;
        }
    }
}
#endif

