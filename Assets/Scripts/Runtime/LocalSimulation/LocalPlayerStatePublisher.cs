using System;
using UnityEngine;
using OverTheHorizon.Player.Input;

namespace OverTheHorizon.LocalSimulation
{
    public readonly struct PlayerKinematicState
    {
        public readonly uint StateTick;
        public readonly Vector3 Position, Velocity, AngularVelocity;
        public readonly Quaternion Rotation;
        public PlayerKinematicState(uint tick, Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity)
        {
            if (!PlayerValueValidation.Finite(position) || !PlayerValueValidation.Finite(rotation)
                || !PlayerValueValidation.Finite(velocity) || !PlayerValueValidation.Finite(angularVelocity))
                throw new InvalidOperationException("Non-finite authoritative body state.");
            StateTick = tick; Position = position; Rotation = rotation; Velocity = velocity; AngularVelocity = angularVelocity;
        }
    }
    public static class LocalPlayerStatePublisher
    {
        public static PlayerKinematicState CaptureEnd(Rigidbody body, uint tick)
            => new PlayerKinematicState(tick, body.position, body.rotation, body.linearVelocity, body.angularVelocity);
        public static PlayerKinematicState CaptureInitial(PlayerPhysicsSuspension suspension)
            => new PlayerKinematicState(0, suspension.Position, suspension.Rotation, suspension.Velocity, suspension.AngularVelocity);
    }
}
