using OverTheHorizon.Simulation;
using Unity.Mathematics;
using UnityEngine;

namespace OverTheHorizon.LocalSimulation
{
    /// <summary>
    /// Local Host authoring for the combat sphere. It deliberately publishes a resolved
    /// per-step sphere instead of exposing this GameObject's Collider to combat systems.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCombatBoundsProvider : MonoBehaviour
    {
        [SerializeField] private Vector3 localCenter;
        [SerializeField, Min(0.0001f)] private float radius = 1f;

        public bool TryBuild(PlayerStepElement frame, out PlayerCombatBoundsStepElement bounds)
        {
            bounds = default;
            if (!isActiveAndEnabled || frame.Valid != 1 || frame.PlayerId == 0 || frame.SlotGeneration == 0
                || frame.Generation == 0 || frame.ConsumedOrdinal == 0 || !math.all(math.isfinite(frame.StartPosition))
                || !math.all(math.isfinite(frame.EndPosition)) || !math.all(math.isfinite(frame.StartRotation.value))
                || !math.all(math.isfinite(frame.EndRotation.value)) || !float.IsFinite(radius) || radius <= 0f)
                return false;

            var offset = new float3(localCenter.x, localCenter.y, localCenter.z);
            float3 startCenter = frame.StartPosition + math.mul(frame.StartRotation, offset);
            float3 endCenter = frame.EndPosition + math.mul(frame.EndRotation, offset);
            if (!math.all(math.isfinite(startCenter)) || !math.all(math.isfinite(endCenter))) return false;

            bounds = new PlayerCombatBoundsStepElement
            {
                Valid = 1,
                PlayerId = frame.PlayerId,
                SlotGeneration = frame.SlotGeneration,
                Generation = frame.Generation,
                Ordinal = frame.ConsumedOrdinal,
                StartCenter = startCenter,
                EndCenter = endCenter,
                Radius = radius
            };
            return true;
        }
    }
}
