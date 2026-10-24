using System.Collections.Generic;
using NUnit.Framework;
using OverTheHorizon.LocalSimulation;
using OverTheHorizon.Simulation;
using Unity.Mathematics;
using UnityEngine;

namespace OverTheHorizon.Tests.EditMode
{
    public sealed class PlayerCombatBoundsProviderTests
    {
        [Test]
        public void TryBuildPublishesResolvedStepSphereAndRejectsInvalidFrame()
        {
            var gameObject = new GameObject("CombatBoundsProviderTest");
            try
            {
                var provider = gameObject.AddComponent<PlayerCombatBoundsProvider>();
                var serialized = new UnityEditor.SerializedObject(provider);
                serialized.FindProperty("localCenter").vector3Value = new Vector3(1f, 0f, 0f);
                serialized.FindProperty("radius").floatValue = 2f;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                var frame = new PlayerStepElement
                {
                    Valid = 1,
                    PlayerId = 7,
                    SlotGeneration = 3,
                    Generation = 11,
                    ConsumedOrdinal = 19,
                    StartPosition = new float3(10f, 0f, 0f),
                    EndPosition = new float3(20f, 0f, 0f),
                    StartRotation = quaternion.identity,
                    EndRotation = quaternion.RotateY(math.radians(90f))
                };

                Assert.IsTrue(provider.TryBuild(frame, out var bounds));
                Assert.That(bounds.StartCenter, Is.EqualTo(new float3(11f, 0f, 0f)).Using(Float3Comparer.Within(0.00001f)));
                Assert.That(bounds.EndCenter, Is.EqualTo(new float3(20f, 0f, -1f)).Using(Float3Comparer.Within(0.00001f)));
                Assert.AreEqual(2f, bounds.Radius);
                Assert.AreEqual(frame.ConsumedOrdinal, bounds.Ordinal);

                frame.PlayerId = 0;
                Assert.IsFalse(provider.TryBuild(frame, out _));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }
    }

    internal sealed class Float3Comparer : IEqualityComparer<float3>
    {
        private readonly float tolerance;

        private Float3Comparer(float tolerance) => this.tolerance = tolerance;

        public static Float3Comparer Within(float tolerance) => new Float3Comparer(tolerance);

        public bool Equals(float3 left, float3 right) => math.all(math.abs(left - right) <= tolerance);

        public int GetHashCode(float3 value) => value.GetHashCode();
    }
}
