using System;
using UnityEngine;
using Unity.Mathematics;
using OverTheHorizon.Player.Input;
using OverTheHorizon.Simulation;
using OverTheHorizon.Timing;

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

        // This class owns the explicit local-physics <-> ECS data boundary.
        // It maps only an already accepted input and captured body state; it does
        // not reserve, complete, or otherwise own the simulation step.
        public static PlayerStepElement BuildStepFrame(uint playerId, PlayerKinematicState completed,
            PlayerControlSnapshot accepted, ulong edgeWatermark, SimulationStepContext context, Vector3 gravity, Rigidbody body)
        {
            var end = CaptureEnd(body, context.Tick);
            return new PlayerStepElement { Valid = 1, PlayerId = playerId, SlotGeneration = 1,
                Generation = context.Generation, ConsumedOrdinal = context.Ordinal, InputSequence = accepted.InputSequence,
                EdgeHighWatermark = edgeWatermark, StartStateTick = completed.StateTick, EndStateTick = end.StateTick,
                StartPosition = ToFloat(completed.Position), StartVelocity = ToFloat(completed.Velocity), StartRotation = ToMath(completed.Rotation),
                EndPosition = ToFloat(end.Position), EndVelocity = ToFloat(end.Velocity), EndAngularVelocity = ToFloat(end.AngularVelocity),
                EndRotation = ToMath(end.Rotation), SteeringRotation = ToMath(accepted.SteeringRotation),
                SteeringForward = ToFloat(accepted.SteeringForward), SteeringRight = ToFloat(accepted.SteeringRight), ViewForward = ToFloat(accepted.ViewForward),
                Axes = ToFloat(accepted.Intent.MoveInput), Gravity = ToFloat(gravity), LogicalYaw = accepted.LogicalYaw,
                LogicalPitch = accepted.LogicalPitch, FreeLook = (byte)(accepted.Intent.FreeLook ? 1 : 0), Maneuver = (byte)(accepted.Intent.Maneuver ? 1 : 0) };
        }
        public static PlayerKinematicState ReadCompleted(PlayerStepElement frame)
            // This only reconstructs a local snapshot. Call it after the caller
            // has committed the frame; it must not be used as success evidence.
            => new PlayerKinematicState(frame.EndStateTick, ToVector(frame.EndPosition), ToUnity(frame.EndRotation),
                ToVector(frame.EndVelocity), ToVector(frame.EndAngularVelocity));
        private static float3 ToFloat(Vector3 value) => new float3(value.x, value.y, value.z);
        private static quaternion ToMath(Quaternion value) => new quaternion(value.x, value.y, value.z, value.w);
        private static Vector3 ToVector(float3 value) => new Vector3(value.x, value.y, value.z);
        private static Quaternion ToUnity(quaternion value) => new Quaternion(value.value.x, value.value.y, value.value.z, value.value.w);
    }
}
