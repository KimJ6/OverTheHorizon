using UnityEngine;
using System;
using OverTheHorizon.Player.Input;

namespace OverTheHorizon.Player.Flight
{
    /// <summary>Flight calculation state only. Does not restore PhysX contacts or body pose.</summary>
    public readonly struct FlightRuntimeSnapshot
    {
        public readonly Vector3 ControlledDirection, ControlledVelocity, GravityVelocity, RawCommand, CommandDirection;
        public readonly float ControlledSpeed, PropulsionAuthority, RawCommandMagnitude;
        public readonly bool CommandActive;
        public readonly int SettingsVersion;
        public FlightRuntimeSnapshot(MotionState s, int settingsVersion)
        {
            ControlledDirection = s.ControlledDirection; ControlledVelocity = s.ControlledVelocity;
            GravityVelocity = s.GravityVelocity; RawCommand = s.RawCommand; CommandDirection = s.CommandDirection;
            ControlledSpeed = s.ControlledSpeed; PropulsionAuthority = s.PropulsionAuthority;
            RawCommandMagnitude = s.RawCommandMagnitude; CommandActive = s.CommandActive; SettingsVersion = settingsVersion;
        }
        public void RestoreTo(MotionState s)
        {
            if (s == null || !PlayerValueValidation.Finite(ControlledDirection) || !PlayerValueValidation.Finite(ControlledVelocity)
                || !PlayerValueValidation.Finite(GravityVelocity) || !PlayerValueValidation.Finite(RawCommand)
                || !PlayerValueValidation.Finite(CommandDirection) || !PlayerValueValidation.Finite(ControlledSpeed)
                || !PlayerValueValidation.Finite(PropulsionAuthority) || !PlayerValueValidation.Finite(RawCommandMagnitude)
                || ControlledDirection.sqrMagnitude < 0.000001f || CommandDirection.sqrMagnitude < 0.000001f
                || ControlledSpeed < 0 || RawCommandMagnitude < 0 || PropulsionAuthority < 0 || PropulsionAuthority > 1)
                throw new InvalidOperationException("Invalid Flight snapshot.");
            s.ControlledDirection = ControlledDirection; s.ControlledVelocity = ControlledVelocity;
            s.GravityVelocity = GravityVelocity; s.RawCommand = RawCommand; s.CommandDirection = CommandDirection;
            s.ControlledSpeed = ControlledSpeed; s.PropulsionAuthority = PropulsionAuthority;
            s.RawCommandMagnitude = RawCommandMagnitude; s.CommandActive = CommandActive;
        }
    }
}
