using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.LowLevel;
using OverTheHorizon.LocalSimulation;

namespace OverTheHorizon.Tests
{
    public sealed class TimingHookTests
    {
        private PlayerLoopSystem original;
        private LocalFixedStepDriver driver;
        [SetUp] public void Setup()
        { original = PlayerLoop.GetCurrentPlayerLoop(); driver = new LocalFixedStepDriver(() => { }, () => { }); }
        [TearDown] public void Restore()
        { try { driver?.Dispose(); } finally { PlayerLoop.SetPlayerLoop(original); } }

        [TestCase("early-post")] [TestCase("late-pre")] [TestCase("delegate")]
        [TestCase("physics")] [TestCase("missing-script")] [TestCase("duplicate-post")]
        [TestCase("other-parent")] [TestCase("duplicate-fixed")]
        public void InvalidStructureIsRejectedEvenWhenMarkersSurvive(string change)
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            Mutate(ref loop, change);
            PlayerLoop.SetPlayerLoop(loop);
            Assert.Throws<InvalidOperationException>(driver.ValidateInstalled);
        }
        [Test] public void UnrelatedSiblingIsAllowed()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop(); Mutate(ref loop, "unrelated"); PlayerLoop.SetPlayerLoop(loop);
            Assert.DoesNotThrow(driver.ValidateInstalled);
        }
        [Test] public void BridgeRetainsOnlyThePhysicsCompletionFence()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            Assert.AreEqual(1, CountNamed(loop, "PreTimingHook"));
            Assert.AreEqual(1, CountNamed(loop, "PhysicsTimingFenceHook"));
            Assert.AreEqual(1, CountNamed(loop, "PostTimingHook"));
            Assert.AreEqual(0, CountNamed(loop, "ScriptTimingFenceHook"));
        }
        private sealed class Unrelated { }
        private static int CountNamed(PlayerLoopSystem node, string name)
        {
            int count = node.type != null && node.type.Name == name ? 1 : 0;
            if (node.subSystemList != null) foreach (var child in node.subSystemList) count += CountNamed(child, name);
            return count;
        }
        private static void Mutate(ref PlayerLoopSystem node, string change)
        {
            if (node.type == typeof(UnityEngine.PlayerLoop.FixedUpdate))
            {
                var list = new List<PlayerLoopSystem>(node.subSystemList);
                int pre = list.FindIndex(x => x.type.Name == "PreTimingHook");
                int post = list.FindIndex(x => x.type.Name == "PostTimingHook");
                if (change == "early-post") { var hook = list[post]; list.RemoveAt(post); list.Insert(pre + 2, hook); }
                if (change == "late-pre") { var hook = list[pre]; list.RemoveAt(pre); list.Insert(pre + 1, hook); }
                if (change == "delegate") { var hook = list[post]; hook.updateDelegate = () => { }; list[post] = hook; }
                if (change == "physics")
                {
                    int i = list.FindIndex(x => x.type == typeof(UnityEngine.PlayerLoop.FixedUpdate.PhysicsFixedUpdate));
                    var physics = list[i]; physics.updateFunction = IntPtr.Zero; physics.updateDelegate = () => { }; list[i] = physics;
                }
                if (change == "missing-script") list.RemoveAll(x => x.type == typeof(UnityEngine.PlayerLoop.FixedUpdate.ScriptRunBehaviourFixedUpdate));
                if (change == "duplicate-post") list.Add(list[post]);
                if (change == "other-parent") { var hook = list[post]; list[post] = new PlayerLoopSystem { type = typeof(Unrelated), subSystemList = new[] { hook } }; }
                if (change == "unrelated") list.Insert(0, new PlayerLoopSystem { type = typeof(Unrelated), updateDelegate = () => { } });
                if (change == "duplicate-fixed") list.Add(new PlayerLoopSystem { type = typeof(UnityEngine.PlayerLoop.FixedUpdate) });
                node.subSystemList = list.ToArray(); return;
            }
            if (node.subSystemList != null) for (int i = 0; i < node.subSystemList.Length; ++i) Mutate(ref node.subSystemList[i], change);
        }
    }
}
