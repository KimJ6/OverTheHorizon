using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;
using FixedPhase = UnityEngine.PlayerLoop.FixedUpdate;

namespace OverTheHorizon.LocalSimulation
{
    public sealed class LocalFixedStepDriver : IDisposable
    {
        private sealed class PreTimingHook { }
        private sealed class PostTimingHook { }
        private static LocalFixedStepDriver active;
        private bool installed;
        private Action preCallback, postCallback;
        private int callbackDepth;
        public bool IsExecuting => callbackDepth != 0;
        public LocalFixedStepDriver(Action pre, Action post)
        {
            if (active != null) throw new InvalidOperationException("A local step driver already exists.");
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            if (Count(loop, typeof(PreTimingHook)) != 0 || Count(loop, typeof(PostTimingHook)) != 0)
                throw new InvalidOperationException("Duplicate timing hooks.");
            preCallback = pre; postCallback = post;
            int groups = 0; Insert(ref loop, InvokePre, InvokePost, ref groups);
            if (groups != 1) throw new InvalidOperationException("Expected exactly one FixedUpdate branch.");
            PlayerLoop.SetPlayerLoop(loop); installed = true; active = this;
        }
        public void ValidateInstalled()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            if (!installed || Count(loop, typeof(PreTimingHook)) != 1 || Count(loop, typeof(PostTimingHook)) != 1)
                throw new InvalidOperationException("Timing hook registration was replaced.");
        }
        private static void Insert(ref PlayerLoopSystem node, Action pre, Action post, ref int groups)
        {
            if (node.type == typeof(FixedPhase))
            {
                ++groups;
                var children = node.subSystemList;
                if (children == null) throw new InvalidOperationException("FixedUpdate has no children.");
                int script = 0, physics = 0; var list = new List<PlayerLoopSystem>(children.Length + 2);
                foreach (var child in children)
                {
                    if (child.type == typeof(FixedPhase.ScriptRunBehaviourFixedUpdate))
                    { ++script; list.Add(new PlayerLoopSystem { type = typeof(PreTimingHook), updateDelegate = () => pre() }); }
                    list.Add(child);
                    if (child.type == typeof(FixedPhase.PhysicsFixedUpdate))
                    { ++physics; list.Add(new PlayerLoopSystem { type = typeof(PostTimingHook), updateDelegate = () => post() }); }
                }
                if (script != 1 || physics != 1) throw new InvalidOperationException("Required fixed phases are missing or duplicated.");
                int scriptIndex = Array.FindIndex(children, x => x.type == typeof(FixedPhase.ScriptRunBehaviourFixedUpdate));
                int physicsIndex = Array.FindIndex(children, x => x.type == typeof(FixedPhase.PhysicsFixedUpdate));
                if (scriptIndex >= physicsIndex) throw new InvalidOperationException("Unsupported fixed phase order.");
                node.subSystemList = list.ToArray(); return;
            }
            if (node.subSystemList == null) return;
            for (int i = 0; i < node.subSystemList.Length; ++i) Insert(ref node.subSystemList[i], pre, post, ref groups);
        }
        private static int Count(PlayerLoopSystem node, Type type)
        { int n = node.type == type ? 1 : 0; if (node.subSystemList != null) foreach (var child in node.subSystemList) n += Count(child, type); return n; }
        private static void Remove(ref PlayerLoopSystem node)
        {
            if (node.subSystemList == null) return;
            var list = new List<PlayerLoopSystem>(node.subSystemList.Length);
            foreach (var original in node.subSystemList)
            {
                if (original.type == typeof(PreTimingHook) || original.type == typeof(PostTimingHook)) continue;
                var child = original; Remove(ref child); list.Add(child);
            }
            node.subSystemList = list.ToArray();
        }
        public void Dispose()
        {
            Deactivate();
            if (!installed) return;
            var loop = PlayerLoop.GetCurrentPlayerLoop(); Remove(ref loop); PlayerLoop.SetPlayerLoop(loop);
            installed = false; if (active == this) active = null;
        }
        public void Deactivate() { preCallback = postCallback = null; }
        private void InvokePre() { Invoke(preCallback); }
        private void InvokePost() { Invoke(postCallback); }
        private void Invoke(Action callback)
        {
            if (callback == null) return;
            ++callbackDepth;
            try { callback(); } finally { --callbackDepth; }
        }
        internal static void ReleaseActive() { active?.Dispose(); }
    }

    // Runs while timeScale is zero and survives destruction of the originating MonoBehaviour.
    internal static class LocalCleanupRecovery
    {
        private sealed class RecoveryHook { }
        internal interface IWork
        {
            void Cleanup();
            void Hold();
            string CleanupStage { get; }
        }
        private static readonly List<IWork> pending = new List<IWork>();
        public static int PendingCount => pending.Count + (LocalTimingOwner.HasFailedAcquisition ? 1 : 0);
        public static string LastError { get; private set; }
        public static string Stage => pending.Count != 0 ? pending[0].CleanupStage : LocalTimingOwner.HasFailedAcquisition ? "TimingAcquisition" : null;
        internal static void EnsureInstalled()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            int count = CountRecovery(loop);
            if (count == 1) return;
            if (count != 0) throw new InvalidOperationException("Duplicate cleanup recovery hooks.");
            int branches = 0; InsertRecovery(ref loop, ref branches);
            if (branches != 1) throw new InvalidOperationException("Cleanup recovery requires one Update branch.");
            PlayerLoop.SetPlayerLoop(loop);
        }
        internal static void RequireDrained()
        {
            EnsureInstalled();
            if (PendingCount != 0)
                throw new InvalidOperationException("Previous session cleanup is pending at " + Stage + ": " + LastError);
        }
        internal static void Retain(IWork work)
        { if (!pending.Contains(work)) pending.Add(work); }
        internal static void Retry()
        {
            try { LocalTimingOwner.RetryFailedAcquisition(); }
            catch (Exception error) { RecordError("TimingAcquisition: " + error.Message); }
            for (int i = pending.Count - 1; i >= 0; --i)
            {
                try { pending[i].Hold(); pending[i].Cleanup(); pending.RemoveAt(i); }
                catch (Exception error)
                {
                    string message = pending[i].CleanupStage + ": " + error.Message;
                    RecordError(message);
                }
            }
            if (PendingCount == 0) LastError = null;
        }
        private static void RecordError(string message)
        { if (message != LastError) Debug.LogError("Timing recovery pending: " + message); LastError = message; }
        private static int CountRecovery(PlayerLoopSystem node)
        {
            int count = node.type == typeof(RecoveryHook) ? 1 : 0;
            if (node.subSystemList != null) foreach (var child in node.subSystemList) count += CountRecovery(child);
            return count;
        }
        private static void InsertRecovery(ref PlayerLoopSystem node, ref int branches)
        {
            if (node.type == typeof(UnityEngine.PlayerLoop.Update))
            {
                ++branches;
                var list = new List<PlayerLoopSystem>(node.subSystemList ?? Array.Empty<PlayerLoopSystem>());
                list.Add(new PlayerLoopSystem { type = typeof(RecoveryHook), updateDelegate = Retry });
                node.subSystemList = list.ToArray(); return;
            }
            if (node.subSystemList == null) return;
            for (int i = 0; i < node.subSystemList.Length; ++i) InsertRecovery(ref node.subSystemList[i], ref branches);
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Never discard failed work when domain reload is disabled.
            EnsureInstalled(); Retry();
            if (PendingCount != 0) return;
            try { LocalFixedStepDriver.ReleaseActive(); LocalTimingOwner.ReleaseActive(); }
            catch (Exception error) { Debug.LogException(error); }
        }
    }
}
