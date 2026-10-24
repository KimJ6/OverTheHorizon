using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using NUnit.Framework;
using Unity.Entities;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.TestTools;
using OverTheHorizon.LocalSimulation;
using OverTheHorizon.Player.Flight;
using OverTheHorizon.Player.Input;
using OverTheHorizon.Player.View;
using OverTheHorizon.Timing;

namespace OverTheHorizon.Tests
{
    // Headless Editor baseline, not a player-build performance budget.
    public sealed class TimingCostTests
    {
        private sealed class SampleStart { }
        private sealed class SampleEnd { }
        [UnityTest] public IEnumerator FourPlayersAt50Hz() { yield return Measure(50); }
        [UnityTest] public IEnumerator FourPlayersAt60Hz() { yield return Measure(60); }
        [UnityTest] public IEnumerator FourPlayersAt120Hz() { yield return Measure(120); }
        private static void Set(object target, string field, object value)
            => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static IEnumerator Measure(int rate)
        {
            var previousWorld = World.DefaultGameObjectInjectionWorld;
            var world = new World("Four player cost " + rate); World.DefaultGameObjectInjectionWorld = world;
            var objects = new List<GameObject>(); GameObject rootObject = null;
            var settings = ScriptableObject.CreateInstance<SimulationTimingSettings>(); Set(settings, "tickRate", rate);
            var savedLoop = PlayerLoop.GetCurrentPlayerLoop();
            ProfilerRecorder stepGc = default, frameGc = default;
            try
            {
                var players = new PlayerFlightController[4];
                for (int i = 0; i < 4; ++i)
                {
                    var go = new GameObject("Cost player " + i); objects.Add(go); go.SetActive(false);
                    go.AddComponent<Rigidbody>(); go.AddComponent<PlayerInputReader>(); go.AddComponent<PlayerViewState>();
                    players[i] = go.AddComponent<PlayerFlightController>(); go.SetActive(true);
                }
                rootObject = new GameObject("Cost timing root"); rootObject.SetActive(false);
                var root = rootObject.AddComponent<LocalSimulationBootstrap>();
                Set(root, "players", players); Set(root, "autoStart", false); Set(root, "pauseOnFocusLost", false); Set(root, "timingSettings", settings);
                rootObject.SetActive(true);
                double deadline = Time.realtimeSinceStartupAsDouble + 15;
                while (root.State != SimulationLifecycle.Ready && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                Assert.AreEqual(SimulationLifecycle.Ready, root.State);
                // Ready and Paused query suppression is asserted separately by lifecycle tests.
                root.RequestStart();
                while (root.CompletedOrdinal < 10 && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                Assert.AreEqual(SimulationLifecycle.Running, root.State);
                stepGc = new ProfilerRecorder(ProfilerCategory.Memory, "GC.Alloc", 1,
                    ProfilerRecorderOptions.WrapAroundWhenCapacityReached | ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
                Assert.IsTrue(stepGc.Valid);
                // Unity's Mono allocation-byte API may return zero. Calibrate the real allocation recorder.
                stepGc.Start(); var calibration = new byte[4096]; GC.KeepAlive(calibration); stepGc.Stop();
                Assert.Greater(stepGc.Count > 0 ? stepGc.GetSample(0).Count : 0, 0, "Allocation recorder did not detect the calibration allocation.");
                stepGc.Reset();
                frameGc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
                Assert.IsTrue(frameGc.Valid);
                long started = 0, stepTicks = 0, stepAllocations = 0;
                int steps = 0, frames = 0; long frameTicks = 0, frameBytes = 0;
                var loop = PlayerLoop.GetCurrentPlayerLoop();
                InsertProbes(ref loop,
                    () => { stepGc.Reset(); stepGc.Start(); started = Stopwatch.GetTimestamp(); },
                    () => { stepTicks += Stopwatch.GetTimestamp() - started; stepGc.Stop(); stepAllocations += stepGc.Count > 0 ? stepGc.GetSample(0).Count : 0; ++steps; });
                PlayerLoop.SetPlayerLoop(loop);
                while (steps < 100 && Time.realtimeSinceStartupAsDouble < deadline)
                {
                    long timestamp = Stopwatch.GetTimestamp();
                    yield return null;
                    frameTicks += Stopwatch.GetTimestamp() - timestamp;
                    frameBytes += frameGc.LastValue; ++frames;
                }
                Assert.GreaterOrEqual(steps, 100); Assert.AreEqual(SimulationLifecycle.Running, root.State);
                Assert.AreEqual(0L, stepAllocations, "The measured fixed-step window must not allocate managed objects.");
                TestContext.Out.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "TIMING_COST rate={0} players=4 steps={1} step_us={2:F3} step_gc_allocations={3:F1} frames={4} frame_interval_us={5:F3} frame_gc_bytes={6:F1}",
                    rate, steps, stepTicks * 1000000.0 / Stopwatch.Frequency / steps, stepAllocations / (double)steps,
                    frames, frameTicks * 1000000.0 / Stopwatch.Frequency / frames, frameBytes / (double)frames));
            }
            finally
            {
                stepGc.Dispose(); frameGc.Dispose();
                // Remove probes before releasing the driver. Restore the original unrelated systems exactly.
                PlayerLoop.SetPlayerLoop(savedLoop);
                if (rootObject != null) UnityEngine.Object.DestroyImmediate(rootObject);
                foreach (var go in objects) UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(settings);
                World.DefaultGameObjectInjectionWorld = previousWorld; world.Dispose();
            }
        }
        private static void InsertProbes(ref PlayerLoopSystem node, PlayerLoopSystem.UpdateFunction begin, PlayerLoopSystem.UpdateFunction end)
        {
            if (node.type == typeof(UnityEngine.PlayerLoop.FixedUpdate))
            {
                var list = new List<PlayerLoopSystem>();
                foreach (var child in node.subSystemList)
                {
                    if (child.type.Name == "PreTimingHook") list.Add(new PlayerLoopSystem { type = typeof(SampleStart), updateDelegate = begin });
                    list.Add(child);
                    if (child.type.Name == "PostTimingHook") list.Add(new PlayerLoopSystem { type = typeof(SampleEnd), updateDelegate = end });
                }
                node.subSystemList = list.ToArray(); return;
            }
            if (node.subSystemList != null) for (int i = 0; i < node.subSystemList.Length; ++i) InsertProbes(ref node.subSystemList[i], begin, end);
        }
    }
}
