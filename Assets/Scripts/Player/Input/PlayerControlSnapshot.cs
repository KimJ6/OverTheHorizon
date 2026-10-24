using UnityEngine;
using OverTheHorizon.Player.View;

namespace OverTheHorizon.Player.Input
{
    public readonly struct RawControlSnapshot
    {
        public readonly ulong Sequence;
        public readonly ControlIntent Intent;
        public readonly Vector2 LookDelta;
        public readonly bool Valid;
        public RawControlSnapshot(ulong sequence, ControlIntent intent, Vector2 lookDelta, bool valid)
        { Sequence = sequence; Intent = intent; LookDelta = lookDelta; Valid = valid; }
    }

    /// <summary>Accepted intent, not a rendered camera pose. Right preserves the existing yaw-only A/D axis.</summary>
    public readonly struct PlayerControlSnapshot
    {
        public readonly ulong Generation;
        public readonly uint PlayerId;
        public readonly ulong InputSequence;
        public readonly ControlIntent Intent;
        public readonly Quaternion SteeringRotation;
        public readonly Vector3 SteeringForward, SteeringRight, ViewForward;
        public readonly float LogicalYaw, LogicalPitch;
        public readonly bool Recentering;
        public readonly bool Valid;
        public PlayerControlSnapshot(ulong generation, uint playerId, RawControlSnapshot raw, PlayerViewState view)
        {
            Generation = generation; PlayerId = playerId; InputSequence = raw.Sequence; Intent = raw.Intent;
            SteeringRotation = view.SteeringRotation;
            SteeringForward = view.SteeringForward; SteeringRight = view.SteeringRight; ViewForward = view.ViewForward;
            LogicalYaw = view.ViewYaw; LogicalPitch = view.ViewPitch; Recentering = view.IsCameraRecentering;
            Valid = generation != 0 && playerId != 0 && raw.Valid && view.IsInitialized
                && raw.Sequence == view.LastAppliedRawSequence && PlayerValueValidation.Finite(SteeringRotation)
                && PlayerValueValidation.Finite(SteeringForward) && PlayerValueValidation.Finite(SteeringRight)
                && PlayerValueValidation.Finite(ViewForward) && PlayerValueValidation.Finite(LogicalYaw)
                && PlayerValueValidation.Finite(LogicalPitch);
        }
    }

    public static class PlayerValueValidation
    {
        public static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        public static bool Finite(Vector3 x) => Finite(x.x) && Finite(x.y) && Finite(x.z);
        public static bool Finite(Quaternion x) => Finite(x.x) && Finite(x.y) && Finite(x.z) && Finite(x.w);
    }
}
