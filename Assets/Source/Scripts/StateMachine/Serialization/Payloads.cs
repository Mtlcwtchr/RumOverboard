using UnityEngine;

namespace RumOverboard.StateMachine.Serialization
{
    // Payload DATA definitions live here; each struct's (de)serialization lives in
    // Payloads.Serialization.cs via `partial`, so wire logic never clutters the model.

    /// <summary>Broadcast when a crew member latches onto a climbable (which one + where).</summary>
    public partial struct ClimbAttachPayload : IStatePayload
    {
        public int ClimbableId;    // instance id of the Climbable the crew grabbed
        public Vector3 LocalPoint; // grab point in the climbable's local space

        public PayloadType Type => PayloadType.ClimbAttach;
    }

    /// <summary>Broadcast while drinking rum — drives the sip animation / intoxication UI.</summary>
    public partial struct RumSipPayload : IStatePayload
    {
        public float Progress; // 0..1 through the current sip
        public byte SipCount;  // how many sips so far this session

        public PayloadType Type => PayloadType.RumSip;
    }
}
