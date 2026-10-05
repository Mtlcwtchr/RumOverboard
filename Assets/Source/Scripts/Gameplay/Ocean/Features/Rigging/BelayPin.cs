#if FUSION2
using RumOverboard.Gameplay.Interaction;
using RumOverboard.Networking;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Features.Rigging
{
    /// <summary>
    /// A belaying pin on a pin rail: any line can be made fast here (if it reaches) and cast off
    /// again. Pure data + prompts; the line state lives on the ship, RopeSystem does the work.
    ///   hands free + a line on the pin → "[E] Отвязать: Фал (фок)"
    ///   holding a line + free pin       → "[E] Закрепить на нагеле"
    /// </summary>
    public sealed class BelayPin : Interactable
    {
        [Tooltip("Rail name shown in prompts, e.g. \"нагель фока (левый борт)\".")]
        [SerializeField] private string railName = "нагель";
        [Tooltip("Where the rope wraps (top of the pin). Falls back to this transform.")]
        [SerializeField] private Transform tiePoint;

        private NetworkShip _ship;

        public override InteractionKind Kind => InteractionKind.BelayPin;
        public NetworkShip Ship => _ship != null ? _ship : (_ship = GetComponentInParent<NetworkShip>());
        public Vector3 TiePoint => tiePoint != null ? tiePoint.position : transform.position;
        public string RailName => railName;

        public void Configure(string rail, Transform tie)
        {
            railName = rail;
            tiePoint = tie;
        }

        public int TiedLine => HasShip ? Ship.LineTiedTo(this) : -1;
        private bool HasShip => Ship != null && Ship.Object != null && Ship.Object.IsValid;

        public override bool IsAvailable(in InteractorInfo who)
        {
            if (!HasShip) return false;
            int tied = TiedLine;
            if (who.HandsFree)
                return tied >= 0; // cast off what's on it
            if (who.HeldShip != Ship || tied >= 0)
                return false;     // pin already taken
            RigLine line = Ship.Line(who.HeldLine);
            return line != null &&
                   Vector3.Distance(line.Block.position, TiePoint) <= Ship.GetLineOut(who.HeldLine) + RiggingConfig.Active.TieSlack;
        }

        public override string GetPrompt(in InteractorInfo who)
        {
            if (!HasShip) return null;
            int tied = TiedLine;
            if (who.HandsFree)
                return tied >= 0 ? $"Отвязать: {Ship.Line(tied).DisplayName}" : $"Свободный {railName}";
            if (tied >= 0)
                return $"Нагель занят: {Ship.Line(tied).DisplayName}";
            return IsAvailable(who) ? $"Закрепить на нагеле ({railName})" : "Канат не достаёт до этого нагеля";
        }
    }
}
#endif
