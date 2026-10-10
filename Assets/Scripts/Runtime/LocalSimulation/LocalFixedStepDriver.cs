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
        // This is the one retained phase fence: Post must never observe a body
        // before Unity's native PhysicsFixedUpdate phase has completed.
        private sealed class PhysicsTimingFenceHook { }
        private sealed class PostTimingHook { }
        private static LocalFixedStepDriver active;
        private bool installed;
        private Action preCallback, postCallback;
        private readonly PlayerLoopSystem.UpdateFunction preDelegate, physicsFenceDelegate, postDelegate;
        private PlayerLoopSystem scriptBaseline, physicsBaseline;
        private int structureValidationCount;
        private int callbackDepth;
        private int executionPhase;
        private string executionOrderError;
        public bool IsExecuting => callbackDepth != 0;
        public LocalFixedStepDriver(Action pre, Action post)
        {
            if (active != null) throw new InvalidOperationException("A local step driver already exists.");
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            if (Count(loop, typeof(PreTimingHook)) != 0 || Count(loop, typeof(PhysicsTimingFenceHook)) != 0
                || Count(loop, typeof(PostTimingHook)) != 0)
                throw new InvalidOperationException("Duplicate timing hooks.");
            preCallback = pre ?? throw new ArgumentNullException(nameof(pre));
            postCallback = post ?? throw new ArgumentNullException(nameof(post));
            preDelegate = InvokePre; physicsFenceDelegate = InvokePhysicsFence; postDelegate = InvokePost;
            int groups = 0; Insert(ref loop, preDelegate, physicsFenceDelegate, postDelegate, ref groups);
            if (groups != 1) throw new InvalidOperationException("Expected exactly one FixedUpdate branch.");
            // Only the standard native Script/Physics phases are supported by this adapter.
            var fixedBranch = Find(PlayerLoop.GetDefaultPlayerLoop(), typeof(FixedPhase));
            scriptBaseline = Find(fixedBranch, typeof(FixedPhase.ScriptRunBehaviourFixedUpdate));
            physicsBaseline = Find(fixedBranch, typeof(FixedPhase.PhysicsFixedUpdate));
            ValidateStructure(loop);
            PlayerLoop.SetPlayerLoop(loop); installed = true; active = this;
        }
        public void ValidateInstalled()
        {
            // R5 boundary check: creation and Ready/Paused -> Running validate the
            // installed loop once. Fixed steps deliberately do not traverse the
            // PlayerLoop; the callbacks below detect their own live ordering.
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            ++structureValidationCount;
            if (!installed || active != this) throw new InvalidOperationException("Timing driver is not active.");
            ValidateStructure(loop);
        }
        public void ValidateExecutionOrder()
        {
            if (!installed || active != this) throw new InvalidOperationException("Timing driver is not active.");
            if (executionOrderError != null) throw new InvalidOperationException(executionOrderError);
        }
        private void ValidateStructure(PlayerLoopSystem loop)
        {
            if (Count(loop, typeof(FixedPhase)) != 1 || Count(loop, typeof(PreTimingHook)) != 1
                || Count(loop, typeof(PhysicsTimingFenceHook)) != 1
                || Count(loop, typeof(PostTimingHook)) != 1
                || Count(loop, typeof(FixedPhase.ScriptRunBehaviourFixedUpdate)) != 1
                || Count(loop, typeof(FixedPhase.PhysicsFixedUpdate)) != 1)
                throw new InvalidOperationException("Timing hook registration was replaced.");
            var children = Find(loop, typeof(FixedPhase)).subSystemList;
            int pre = -1, script = -1, physics = -1, physicsFence = -1, post = -1;
            if (children != null) for (int i = 0; i < children.Length; ++i)
            {
                var type = children[i].type;
                if (type == typeof(PreTimingHook)) pre = i;
                if (type == typeof(PhysicsTimingFenceHook)) physicsFence = i;
                if (type == typeof(PostTimingHook)) post = i;
                if (type == typeof(FixedPhase.ScriptRunBehaviourFixedUpdate)) script = i;
                if (type == typeof(FixedPhase.PhysicsFixedUpdate)) physics = i;
            }
            if (pre < 0 || script <= pre || physics <= script || physicsFence <= physics || post <= physicsFence)
                throw new InvalidOperationException("Required order is Pre, Script, Physics, Post in one FixedUpdate branch.");
            if (!SameManagedExecution(children[pre], preDelegate)
                || !SameManagedExecution(children[physicsFence], physicsFenceDelegate)
                || !SameManagedExecution(children[post], postDelegate)
                || !SameExecution(children[script], scriptBaseline) || !SameExecution(children[physics], physicsBaseline))
                throw new InvalidOperationException("Timing hook or mandatory phase execution was replaced.");
        }
        private static bool SameManagedExecution(PlayerLoopSystem value, PlayerLoopSystem.UpdateFunction callback)
            => value.updateDelegate == callback && value.updateFunction == IntPtr.Zero
                && value.loopConditionFunction == IntPtr.Zero && value.subSystemList == null;
        private static bool SameExecution(PlayerLoopSystem value, PlayerLoopSystem baseline)
            => value.updateDelegate == baseline.updateDelegate && value.updateFunction == baseline.updateFunction
                && value.loopConditionFunction == baseline.loopConditionFunction
                && value.subSystemList == null && baseline.subSystemList == null;
        private static PlayerLoopSystem Find(PlayerLoopSystem node, Type type)
        {
            if (node.type == type) return node;
            if (node.subSystemList != null) foreach (var child in node.subSystemList)
            { var found = Find(child, type); if (found.type == type) return found; }
            return default;
        }
        private static void Insert(ref PlayerLoopSystem node, PlayerLoopSystem.UpdateFunction pre,
            PlayerLoopSystem.UpdateFunction physicsFence, PlayerLoopSystem.UpdateFunction post, ref int groups)
        {
            if (node.type == typeof(FixedPhase))
            {
                ++groups;
                var children = node.subSystemList;
                if (children == null) throw new InvalidOperationException("FixedUpdate has no children.");
                int script = 0, physics = 0; var list = new List<PlayerLoopSystem>(children.Length + 3);
                foreach (var child in children)
                {
                    if (child.type == typeof(FixedPhase.ScriptRunBehaviourFixedUpdate))
                    { ++script; list.Add(new PlayerLoopSystem { type = typeof(PreTimingHook), updateDelegate = pre }); }
                    list.Add(child);
                    if (child.type == typeof(FixedPhase.PhysicsFixedUpdate))
                    {
                        ++physics;
                        list.Add(new PlayerLoopSystem { type = typeof(PhysicsTimingFenceHook), updateDelegate = physicsFence });
                        list.Add(new PlayerLoopSystem { type = typeof(PostTimingHook), updateDelegate = post });
                    }
                }
                if (script != 1 || physics != 1) throw new InvalidOperationException("Required fixed phases are missing or duplicated.");
                int scriptIndex = Array.FindIndex(children, x => x.type == typeof(FixedPhase.ScriptRunBehaviourFixedUpdate));
                int physicsIndex = Array.FindIndex(children, x => x.type == typeof(FixedPhase.PhysicsFixedUpdate));
                if (scriptIndex >= physicsIndex) throw new InvalidOperationException("Unsupported fixed phase order.");
                node.subSystemList = list.ToArray(); return;
            }
            if (node.subSystemList == null) return;
            for (int i = 0; i < node.subSystemList.Length; ++i)
                Insert(ref node.subSystemList[i], pre, physicsFence, post, ref groups);
        }
        private static int Count(PlayerLoopSystem node, Type type)
        { int n = node.type == type ? 1 : 0; if (node.subSystemList != null) foreach (var child in node.subSystemList) n += Count(child, type); return n; }
        private static void Remove(ref PlayerLoopSystem node)
        {
            if (node.subSystemList == null) return;
            var list = new List<PlayerLoopSystem>(node.subSystemList.Length);
            foreach (var original in node.subSystemList)
            {
                if (original.type == typeof(PreTimingHook) || original.type == typeof(PhysicsTimingFenceHook)
                    || original.type == typeof(PostTimingHook)) continue;
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
        private void InvokePre()
        {
            if (executionPhase != 0) RecordExecutionOrder("A fixed step began before the previous timing hook sequence closed.");
            executionPhase = 1; Invoke(preCallback);
        }
        private void InvokePhysicsFence()
        {
            if (executionPhase != 1) RecordExecutionOrder("PhysicsFixedUpdate did not follow the pre timing hook.");
            executionPhase = 2;
        }
        private void InvokePost()
        {
            if (executionPhase != 2) RecordExecutionOrder("The post timing hook ran before the physics phase completed.");
            executionPhase = 0; Invoke(postCallback);
        }
        private void RecordExecutionOrder(string message)
        { if (executionOrderError == null) executionOrderError = message; }
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
        internal interface IWork
        {
            void Cleanup();
            void Hold();
            string CleanupStage { get; }
        }
        private static readonly List<IWork> pending = new List<IWork>();
        private static LocalCleanupRecoveryHost host;
        private static bool retrying;
        private static int lastAutomaticFrame = -1;
        public static int PendingCount => pending.Count + (LocalTimingOwner.HasFailedAcquisition ? 1 : 0);
        public static string LastError { get; private set; }
        public static string Stage => pending.Count != 0 ? pending[0].CleanupStage : LocalTimingOwner.HasFailedAcquisition ? "TimingAcquisition" : null;
        internal static void RequireDrained()
        {
            EnsureHost();
            if (PendingCount != 0)
                throw new InvalidOperationException("Previous session cleanup is pending at " + Stage + ": " + LastError);
        }
        internal static void Retain(IWork work)
        {
            if (!pending.Contains(work)) pending.Add(work);
            // A persistent MonoBehaviour is the only retry path. Unlike a custom
            // PlayerLoop hook it survives the destroyed Bootstrap without adding
            // another loop owner to the timing bridge.
            try { EnsureHost(); }
            catch (Exception error) { RecordError("RecoveryHost: " + error.Message); }
        }
        private static void EnsureHost()
        {
            if (!Application.isPlaying || host != null) return;
            // A pending SessionResources instance has one continuation owner. A
            // duplicate host is ambiguous, so fail closed instead of selecting one.
            var existing = UnityEngine.Object.FindObjectsByType<LocalCleanupRecoveryHost>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (existing.Length > 1) throw new InvalidOperationException("Duplicate cleanup recovery hosts.");
            if (existing.Length == 1) { host = existing[0]; host.enabled = true; return; }
            var owner = new GameObject("Local timing cleanup recovery");
            UnityEngine.Object.DontDestroyOnLoad(owner);
            host = owner.AddComponent<LocalCleanupRecoveryHost>();
        }
        private static void RetryAutomatic()
        {
            if (PendingCount == 0 || lastAutomaticFrame == Time.frameCount) return;
            lastAutomaticFrame = Time.frameCount;
            Retry();
        }
        internal static void LateRecovery()
        {
            // LateUpdate continues while timeScale is zero and runs after a failed
            // root cleanup has handed its still-owned resources to this host.
            RetryAutomatic();
        }
        internal static void Retry()
        {
            if (retrying) return;
            retrying = true;
            try
            {
                try { LocalTimingOwner.RetryFailedAcquisition(); }
                catch (Exception error) { RecordError("TimingAcquisition: " + error.Message); }
                for (int i = pending.Count - 1; i >= 0; --i)
                {
                    // Keep the work retained until both quarantine and cleanup
                    // return. A failed attempt must continue blocking new sessions.
                    try { pending[i].Hold(); pending[i].Cleanup(); pending.RemoveAt(i); }
                    catch (Exception error)
                    {
                        string message = pending[i].CleanupStage + ": " + error.Message;
                        RecordError(message);
                    }
                }
                if (PendingCount == 0) LastError = null;
            }
            finally { retrying = false; }
        }
        private static void RecordError(string message)
        { if (message != LastError) Debug.LogError("Timing recovery pending: " + message); LastError = message; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Never discard failed work when domain reload is disabled.
            lastAutomaticFrame = -1;
            try { EnsureHost(); } catch (Exception error) { RecordError("RecoveryHost: " + error.Message); }
            Retry();
            if (PendingCount != 0) return;
            try { LocalFixedStepDriver.ReleaseActive(); LocalTimingOwner.ReleaseActive(); }
            catch (Exception error) { Debug.LogException(error); }
        }
    }
}
