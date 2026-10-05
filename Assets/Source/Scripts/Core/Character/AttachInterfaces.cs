using UnityEngine;

namespace RumOverboard.Core.Character
{
    /// <summary>A climbable rail the Climbing state moves along (implemented by ClimbSurface).</summary>
    public interface IClimbRail
    {
        float Length { get; }
        float HalfWidth { get; }
        float SpeedMultiplier { get; }
        Rigidbody Body { get; }
        Transform TopExit { get; }
        Vector3 OutwardWorld { get; }
        Quaternion BodyRotation { get; }
        Vector3 BodyPoint(float u, float v);
        void Project(Vector3 worldGrip, out float u, out float v);
    }

    /// <summary>
    /// A fixed spot on the ship the crew member stands at while operating it (helm, rope station).
    /// The state machine glues the body there with <see cref="AttachMotor"/>.
    /// </summary>
    public interface IStationAnchor
    {
        Vector3 StandPosition { get; }
        Quaternion StandRotation { get; }
        Rigidbody Body { get; }
    }
}
