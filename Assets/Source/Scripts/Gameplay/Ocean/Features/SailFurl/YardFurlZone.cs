#if FUSION2
using RumOverboard.Gameplay.Interaction;
using RumOverboard.Gameplay.Ocean.Features.Masts;
using RumOverboard.Networking;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Features.SailFurl
{
    /// <summary>
    /// Interaction zone along a yard: E furls the sail onto the yard (gaskets) or casts it loose.
    /// Reachable from the top (platform) and from the rigging while climbing
    /// (<see cref="Interactable.UsableWhileClimbing"/>).
    ///
    /// Pure view + routing — the furl state lives on <see cref="NetworkShip"/> (host authority) and
    /// is advanced by <see cref="SailFurlSystem"/>. Also drives the yard's cosmetic pose: the yard spar
    /// and the furled-canvas roll swing with the braced sail around the same pivot as the cloth (the
    /// trigger collider itself stays put: moving colliders on the hull body would disturb its inertia).
    /// </summary>
    public sealed class YardFurlZone : Interactable, IHostUsable
    {
        [Header("Yard")]
        [SerializeField] private ShipMast mast;
        [SerializeField] private int sailIndex;
        [Tooltip("Russian sail name for prompts, e.g. \"фок\" / \"грот-марсель\".")]
        [SerializeField] private string sailLabel = "парус";

        [Header("Visuals (rotated with the yard)")]
        [SerializeField] private Transform yardVisual;
        [SerializeField] private Transform roll;

        private NetworkShip _ship;
        private Transform _shipRoot;
        private Vector3 _yardRestPos, _rollRestPos, _rollRestScale;
        private Quaternion _yardRestRot = Quaternion.identity, _rollRestRot = Quaternion.identity;
        private bool _captured;
        private int _flat = -1;

        public override InteractionKind Kind => InteractionKind.Yard;
        public override bool UsableWhileClimbing => true;

        public ShipMast Mast => mast;
        public int SailIndex => sailIndex;
        public string SailLabel => sailLabel;
        public Transform Roll => roll;

        public NetworkShip Ship
        {
            get
            {
                if (_ship == null)
                    _ship = GetComponentInParent<NetworkShip>(true);
                return _ship;
            }
        }

        /// <summary>Flat sail index on the ship (ShipSailsAggregator order), or -1.</summary>
        public int FlatIndex
        {
            get
            {
                if (_flat < 0 && Ship != null && Ship.Sails != null)
                    _flat = Ship.Sails.FlatIndexOf(mast, sailIndex);
                return _flat;
            }
        }

        public void Configure(ShipMast owner, int index, string label, Transform yard, Transform rollVisual)
        {
            mast = owner;
            sailIndex = index;
            sailLabel = label;
            yardVisual = yard;
            roll = rollVisual;
            _flat = -1;
        }

        private bool Networked => Ship != null && Ship.Object != null && Ship.Object.IsValid && FlatIndex >= 0;

        // ---- Interactable ------------------------------------------------------------------------
        public override string GetPrompt(in InteractorInfo who)
        {
            if (!Networked)
                return Prompt;
            int s = FlatIndex;
            float furl = Ship.GetSailFurl(s);
            bool goalFurled = Ship.GetSailFurlGoal(s);
            int pct = Mathf.RoundToInt(furl * 100f);

            if (!Ship.IsSailFurlInProgress(s))
                return furl >= 0.5f ? $"Раскрутить {sailLabel}" : $"Скрутить {sailLabel}";

            Fusion.PlayerRef worker = Ship.GetSailFurlWorker(s);
            if (worker == who.Player)
                return goalFurled
                    ? $"Передумать — раскрутить {sailLabel} (скручен на {pct}%)"
                    : $"Передумать — скрутить {sailLabel} (скручен на {pct}%)";
            if (worker.IsRealPlayer)
                return goalFurled ? $"{Cap(sailLabel)} уже скручивают ({pct}%)" : $"{Cap(sailLabel)} уже раскручивают ({pct}%)";
            return goalFurled
                ? $"Продолжить скручивать {sailLabel} ({pct}%)"
                : $"Продолжить раскручивать {sailLabel} ({pct}%)";
        }

        public override bool IsAvailable(in InteractorInfo who)
        {
            if (!who.HandsFree || !Networked)
                return false;
            int s = FlatIndex;
            Fusion.PlayerRef worker = Ship.GetSailFurlWorker(s);
            return !(Ship.IsSailFurlInProgress(s) && worker.IsRealPlayer && worker != who.Player);
        }

        public void HostUse(in InteractorInfo who)
        {
            if (Networked && IsAvailable(who))
                Ship.TryUseFurl(FlatIndex, who.Player);
        }

        private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

        // ---- Cosmetics ---------------------------------------------------------------------------
        private void CaptureRest()
        {
            _shipRoot = Ship != null ? Ship.transform : transform.root;
            if (yardVisual != null)
            {
                _yardRestPos = _shipRoot.InverseTransformPoint(yardVisual.position);
                _yardRestRot = Quaternion.Inverse(_shipRoot.rotation) * yardVisual.rotation;
            }
            if (roll != null)
            {
                _rollRestPos = _shipRoot.InverseTransformPoint(roll.position);
                _rollRestRot = Quaternion.Inverse(_shipRoot.rotation) * roll.rotation;
                _rollRestScale = roll.localScale;
            }
            _captured = true;
        }

        private void Awake() => CaptureRest();

        private void LateUpdate()
        {
            if (mast == null || sailIndex < 0 || sailIndex >= mast.SailCount)
                return;
            if (!_captured || _shipRoot == null)
                CaptureRest();

            MaxDistance = SailFurlConfig.Active.ZoneReach;

            ShipMast.SailPanel sail = mast.Sails[sailIndex];
            Vector3 pivot = (sail.topLeftLocal + sail.topRightLocal) * 0.5f;
            Quaternion q = Quaternion.AngleAxis(mast.GetSheetAngle(sailIndex), Vector3.up);

            if (yardVisual != null)
                Place(yardVisual, pivot, q, _yardRestPos, _yardRestRot);

            if (roll != null)
            {
                float furl = mast.GetFurl(sailIndex);
                bool show = furl > 0.03f;
                if (roll.gameObject.activeSelf != show)
                    roll.gameObject.SetActive(show);
                if (show)
                {
                    Place(roll, pivot, q, _rollRestPos, _rollRestRot);
                    float d = SailFurlConfig.Active.RollDiameter * Mathf.Lerp(0.35f, 1f, furl);
                    roll.localScale = new Vector3(d, _rollRestScale.y * Mathf.Lerp(0.85f, 1f, furl), d);
                }
            }
        }

        private void Place(Transform t, Vector3 pivot, Quaternion q, Vector3 restPos, Quaternion restRot)
        {
            Vector3 local = pivot + q * (restPos - pivot);
            t.SetPositionAndRotation(_shipRoot.TransformPoint(local), _shipRoot.rotation * (q * restRot));
        }
    }
}
#endif
