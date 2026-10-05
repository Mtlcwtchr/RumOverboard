#if FUSION2
using RumOverboard.Core.Character;
using RumOverboard.Gameplay.Ocean;
using UnityEngine;

namespace RumOverboard.Gameplay.Interaction
{
    /// <summary>
    /// Interaction data for the ship's wheel. The wheel/rudder dynamics and the replicated
    /// occupancy stay on <see cref="ShipHelm"/>; this only makes the helm lookable/usable and
    /// tells the Steering state where to stand.
    /// </summary>
    public sealed class HelmStation : Interactable, IStationAnchor
    {
        [SerializeField] private ShipHelm helm;
        [SerializeField] private string busyPrompt = "Штурвал занят";

        public override InteractionKind Kind => InteractionKind.Helm;

        public ShipHelm Helm
        {
            get
            {
                if (helm == null)
                    helm = GetComponentInParent<ShipHelm>();
                if (helm == null)
                    helm = GetComponentInChildren<ShipHelm>();
                return helm;
            }
        }

        private Rigidbody _body;
        public Rigidbody Body => _body != null ? _body : (_body = GetComponentInParent<Rigidbody>());

        public void Configure(ShipHelm target) => helm = target;

        public Vector3 StandPosition => Helm != null ? Helm.StandPosition : transform.position;
        public Quaternion StandRotation => Quaternion.Euler(0f, Helm != null ? Helm.FacingYaw : transform.eulerAngles.y, 0f);

        public override bool IsAvailable(in InteractorInfo who)
        {
            ShipHelm h = Helm;
            return who.HandsFree && h != null && h.Object != null && (!h.IsOccupied || h.Occupant == who.Player);
        }

        public override string GetPrompt(in InteractorInfo who) =>
            !who.HandsFree ? "Сначала закрепи или брось канат" : IsAvailable(who) ? Prompt : busyPrompt;

        private void Reset() => Prompt = "Встать за штурвал";
    }
}
#endif
