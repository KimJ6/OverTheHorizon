using UnityEngine;

namespace OverTheHorizon.LocalSimulation
{
    /// <summary>Lifecycle pose writes only. Changes contact state; it is not PhysX rollback.</summary>
    public sealed class PlayerPhysicsSuspension
    {
        private readonly Rigidbody body;
        public Rigidbody Body => body;
        private bool originalKinematic;
        public bool Suspended { get; private set; }
        public Vector3 Position { get; private set; }
        public Quaternion Rotation { get; private set; }
        public Vector3 Velocity { get; private set; }
        public Vector3 AngularVelocity { get; private set; }
        public PlayerPhysicsSuspension(Rigidbody body) { this.body = body; }
        public void Freeze()
        {
            if (body == null) return;
            if (Suspended) { Hold(); return; }
            Position = body.position; Rotation = body.rotation;
            Velocity = body.linearVelocity; AngularVelocity = body.angularVelocity;
            originalKinematic = body.isKinematic; Suspended = true; body.isKinematic = true;
            Hold();
        }
        public void SetResumeState(Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity)
        { Position = position; Rotation = rotation; Velocity = velocity; AngularVelocity = angularVelocity; Hold(); }
        public void Hold()
        {
            if (!Suspended || body == null) return;
            body.isKinematic = true;
            body.position = Position; body.rotation = Rotation;
            // Replace any queued MoveRotation target from a partially failed Flight call.
            body.MoveRotation(Rotation);
        }
        public void Restore()
        {
            if (!Suspended || body == null) return;
            body.position = Position; body.rotation = Rotation; body.isKinematic = originalKinematic;
            if (!body.isKinematic) { body.linearVelocity = Velocity; body.angularVelocity = AngularVelocity; }
            Suspended = false;
        }
    }
}
