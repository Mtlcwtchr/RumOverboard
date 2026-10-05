#if FUSION2
using RumOverboard.Core.Sim;
using RumOverboard.Networking;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Features.Rigging
{
    public enum RigLineKind : byte
    {
        /// <summary>Raises/lowers a sail: more rope hauled down to the deck = sail higher.</summary>
        Halyard = 0,
        /// <summary>
        /// Port brace: runs from the PORT yardarm. Hauling it swings the port arm aft (yard angle
        /// toward −max); easing it lets the wind swing the yard to starboard. Each side of the yard
        /// has its own brace — to swing the yard one way you haul one brace AND ease the other.
        /// </summary>
        BracePort = 1,
        /// <summary>Starboard brace: mirror of <see cref="BracePort"/> (hauling → yard angle toward +max).</summary>
        BraceStarboard = 2,
    }

    public enum RigLineMode : byte
    {
        Tied = 0,  // made fast on a belaying pin
        Held = 1,  // in a crew member's hands
        Loose = 2, // free end: flails, and the load runs the line out
    }

    /// <summary>
    /// Authored data for one running line (ECS-style data component). The line comes down from
    /// aloft (<see cref="Aloft"/>, the sail corner) through a lead block on the mast
    /// (<see cref="Block"/>) to a free end on deck. How much rope is on the deck side of the block
    /// (<c>Out</c>, metres) is what sets the sail: Out = <see cref="OutMin"/> → sail furled,
    /// Out = OutMin + <see cref="HaulRange"/> → fully set. Live state (Out, mode, pin, holder, end
    /// position) is replicated in the ship's line arrays; <see cref="RopeSystem"/> runs it.
    /// </summary>
    public sealed class RigLine : MonoBehaviour
    {
        [SerializeField] private int lineIndex;
        [SerializeField] private RigLineKind kind = RigLineKind.Halyard;
        [Tooltip("Flat sail index in ShipSailsAggregator this line works.")]
        [SerializeField] private int sailIndex;
        [SerializeField] private string displayName = "Фал";

        [Header("Geometry")]
        [Tooltip("Lead block on the mast: the rope turns here from aloft down to the deck.")]
        [SerializeField] private Transform block;
        [Tooltip("Where the rope goes aloft (sail corner / yard). Cosmetic.")]
        [SerializeField] private Transform aloft;
        [Tooltip("The pin the line starts made fast on.")]
        [SerializeField] private BelayPin homePin;
        [Tooltip("Grab handle that follows the free end (enabled only while the end is loose).")]
        [SerializeField] private RopeEnd end;

        [Header("Length")]
        [Tooltip("Shortest deck-side rope (stopper knot): the free end can never run up past this.")]
        [SerializeField] private float outMin = 3f;
        [Tooltip("Rope that has to come down to fully set the sail (m).")]
        [SerializeField] private float haulRange = 5f;
        [Tooltip("Braces only: max yard angle each side (deg).")]
        [UnityEngine.Serialization.FormerlySerializedAs("sheetMaxAngle")]
        [SerializeField] private float braceMaxAngle = 60f;
        [Tooltip("Initial setting 0..1 (halyards start furled = 0).")]
        [Range(0f, 1f)] [SerializeField] private float initialValue;

        [Header("Look")]
        [SerializeField] private Material ropeMaterial;

        private NetworkShip _ship;

        public int LineIndex => lineIndex;
        public RigLineKind Kind => kind;
        public int SailIndex => sailIndex;
        public string DisplayName => displayName;
        public Transform Block => block != null ? block : transform;
        public Transform Aloft => aloft;
        public BelayPin HomePin => homePin;
        public RopeEnd End => end;
        public float OutMin => outMin;
        public float HaulRange => Mathf.Max(0.1f, haulRange);
        public float OutMax => outMin + HaulRange;
        public float BraceMaxAngle => braceMaxAngle;
        public bool IsBrace => kind == RigLineKind.BracePort || kind == RigLineKind.BraceStarboard;
        /// <summary>+1 for the starboard brace (hauls the yard toward +angle), −1 for port, 0 for halyards.</summary>
        public float BraceSign => kind == RigLineKind.BraceStarboard ? 1f : kind == RigLineKind.BracePort ? -1f : 0f;
        public float InitialValue => initialValue;
        public Material RopeMaterial => ropeMaterial;
        public NetworkShip Ship => _ship != null ? _ship : (_ship = GetComponentInParent<NetworkShip>());

        /// <summary>Sail setting 0..1 for a given deck-side rope length.</summary>
        public float ValueFromOut(float outLength) => Mathf.Clamp01((outLength - outMin) / HaulRange);
        public float OutFromValue(float value) => outMin + Mathf.Clamp01(value) * HaulRange;

        public void Configure(int index, RigLineKind lineKind, int sail, string name, Transform blockPoint,
            Transform aloftPoint, BelayPin pin, RopeEnd endHandle, float minOut, float range, float initial, Material material)
        {
            lineIndex = index;
            kind = lineKind;
            sailIndex = sail;
            displayName = name;
            block = blockPoint;
            aloft = aloftPoint;
            homePin = pin;
            end = endHandle;
            outMin = minOut;
            haulRange = range;
            initialValue = initial;
            ropeMaterial = material;
        }

        private void OnEnable() => SimWorld.Add(this);
        private void OnDisable() => SimWorld.Remove(this);

        private void OnDrawGizmosSelected()
        {
            if (block == null) return;
            Gizmos.color = new Color(0.9f, 0.6f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(block.position, 0.12f);
            Gizmos.DrawWireSphere(block.position, outMin);
            if (aloft != null) Gizmos.DrawLine(block.position, aloft.position);
            if (homePin != null) Gizmos.DrawLine(block.position, homePin.transform.position);
        }
    }
}
#endif
