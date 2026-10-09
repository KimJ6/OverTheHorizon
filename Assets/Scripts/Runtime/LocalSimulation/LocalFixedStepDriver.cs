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
        private sealed class ScriptTimingFenceHook { }
        private sealed class PhysicsTimingFenceHook { }
        private sealed class PostTimingHook { }
        private static LocalFixedStepDriver active;
        private bool installed;
        private Action preCallback, postCallback;
        private readonly PlayerLoopSystem.UpdateFunction preDelegate, scriptFenceDelegate, physicsFenceDelegate, postDelegate;
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
            if (Count(loop, typeof(PreTimingHook)) != 0 || Count(loop, typeof(ScriptTimingFenceHook)) != 0
                || Count(loop, typeof(PhysicsTimingFenceHook)) != 0 || Count(loop, typeof(PostTimingHook)) != 0)
                throw new InvalidOperationException("Duplicate timing hooks.");
            preCallback = pre ?? throw new ArgumentNullException(nameof(pre));
            postCallback = post ?? throw new ArgumentNullException(nameof(post));
            preDelegate = InvokePre; scriptFenceDelegate = InvokeScriptFence;
            physicsFenceDelegate = InvokePhysicsFence; postDelegate = InvokePost;
            int groups = 0; Insert(ref loop, preDelegate, scriptFenceDelegate, physicsFenceDelegate, postDelegate, ref groups);
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
                || Count(loop, typeof(ScriptTimingFenceHook)) != 1 || Count(loop, typeof(PhysicsTimingFenceHook)) != 1
                || Count(loop, typeof(PostTimingHook)) != 1
                || Count(loop, typeof(FixedPhase.ScriptRunBehaviourFixedUpdate)) != 1
                || Count(loop, typeof(FixedPhase.PhysicsFixedUpdate)) != 1)
                throw new InvalidOperationException("Timing hook registration was replaced.");
            var children = Find(loop, typeof(FixedPhase)).subSystemList;
            int pre = -1, script = -1, scriptFence = -1, physics = -1, physicsFence = -1, post = -1;
            if (children != null) for (int i = 0; i < children.Length; ++i)
            {
                var type = children[i].type;
                if (type == typeof(PreTimingHook)) pre = i;
                if (type == typeof(ScriptTimingFenceHook)) scriptFence = i;
                if (type == typeof(PhysicsTimingFenceHook)) physicsFence = i;
                if (type == typeof(PostTimingHook)) post = i;
                if (type == typeof(FixedPhase.ScriptRunBehaviourFixedUpdate)) script = i;
                if (type == typeof(FixedPhase.PhysicsFixedUpdate)) physics = i;
            }
            if (pre < 0 || script <= pre || scriptFence <= script || physics <= scriptFence
                || physicsFence <= physics || post <= physicsFence)
                throw new InvalidOperationException("Required order is Pre, Script, Physics, Post in one FixedUpdate branch.");
            if (!SameManagedExecution(children[pre], preDelegate)
                || !SameManagedExecution(children[scriptFence], scriptFenceDelegate)
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
            PlayerLoopSystem.UpdateFunction scriptFence, PlayerLoopSystem.UpdateFunction physicsFence,
            PlayerLoopSystem.UpdateFunction post, ref int groups)
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
                    { ++script; list.Add(new PlayerLoopSystem { type = typeof(PreTimingHook), updateDelegate = pre }); }
                    list.Add(child);
                    if (child.type == typeof(FixedPhase.ScriptRunBehaviourFixedUpdate))
                        list.Add(new PlayerLoopSystem { type = typeof(ScriptTimingFenceHook), updateDelegate = scriptFence });
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
                Insert(ref node.subSystemList[i], pre, scriptFence, physicsFence, post, ref groups);
        }
        private static int Count(PlayerLoopSystem node, Type type)
        { int n = node.type == type ? 1 : 0; if (node.subSystemList != null) foreach (var child in node.subSystemList) n += Count(child, type); return n; }
        private static void Remove(ref PlayerLoopSystem node)
        {
            if (node.subSystemList == null) return;
            var list = new List<PlayerLoopSystem>(node.subSystemList.Length);
            foreach (var original in node.subSystemList)
            {
                if (original.type == typeof(PreTimingHook) || original.type == typeof(ScriptTimingFenceHook)
                    || original.type == typeof(PhysicsTimingFenceHook) || original.type == typeof(PostTimingHook)) continue;
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
        private void InvokeScriptFence()
        {
            if (executionPhase != 1) RecordExecutionOrder("ScriptRunBehaviourFixedUpdate did not follow the pre timing hook.");
            executionPhase = 2;
        }
        private void InvokePhysicsFence()
        {
            if (executionPhase != 2) RecordExecutionOrder("PhysicsFixedUpdate did not follow the script timing phase.");
            executionPhase = 3;
        }
        private void InvokePost()
        {
            if (executionPhase != 3) RecordExecutionOrder("The post timing hook ran before the script and physics phases completed.");
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
        private sealed class RecoveryHook { }
        internal interface IWork
        {
            void Cleanup();
            void Hold();
            string CleanupStage { get; }
        }
        private static readonly List<IWork> pending = new List<IWork>();
        private static LocalCleanupRecoveryHost host;
        private static bool retrying;
        private static int lastAutomaticFrame = -1, lastHookFrame = -1;
        public static int PendingCount => pending.Count + (LocalTimingOwner.HasFailedAcquisition ? 1 : 0);
        public static string LastError { get; private set; }
        public static string Stage => pending.Count != 0 ? pending[0].CleanupStage : LocalTimingOwner.HasFailedAcquisition ? "TimingAcquisition" : null;
        internal static void EnsureInstalled()
        {
            EnsureHost();
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            int count = CountRecovery(loop);
            if (count == 1 && HasValidRecovery(loop)) return;
            RemoveRecovery(ref loop);
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
        {
            if (!pending.Contains(work)) pending.Add(work);
            try { EnsureInstalled(); }
            catch (Exception error) { RecordError("RecoveryInstallation: " + error.Message); }
        }
        private static void EnsureHost()
        {
            if (!Application.isPlaying || host != null) return;
            var existing = UnityEngine.Object.FindObjectsByType<LocalCleanupRecoveryHost>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (existing.Length > 1) throw new InvalidOperationException("Duplicate cleanup recovery hosts.");
            if (existing.Length == 1) { host = existing[0]; host.enabled = true; return; }
            var owner = new GameObject("Local timing cleanup recovery");
            UnityEngine.Object.DontDestroyOnLoad(owner);
            host = owner.AddComponent<LocalCleanupRecoveryHost>();
        }
        private static void RecoveryUpdate()
        {
            if (Time.inFixedTimeStep) return;
            lastHookFrame = Time.frameCount;
            RetryAutomatic();
        }
        private static void RetryAutomatic()
        {
            if (PendingCount == 0 || lastAutomaticFrame == Time.frameCount) return;
            lastAutomaticFrame = Time.frameCount;
            Retry();
        }
        internal static void LateRecovery()
        {
            if (PendingCount == 0 || lastHookFrame == Time.frameCount) return;
            // The independent host still services retained work if the custom hook was removed.
            RetryAutomatic();
            try { EnsureInstalled(); }
            catch (Exception error) { RecordError("RecoveryInstallation: " + error.Message); }
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
        private static int CountRecovery(PlayerLoopSystem node)
        {
            int count = node.type == typeof(RecoveryHook) ? 1 : 0;
            if (node.subSystemList != null) foreach (var child in node.subSystemList) count += CountRecovery(child);
            return count;
        }
        private static bool HasValidRecovery(PlayerLoopSystem node)
        {
            int updates = 0, valid = 0;
            InspectRecovery(node, ref updates, ref valid);
            return updates == 1 && valid == 1;
        }
        private static void InspectRecovery(PlayerLoopSystem node, ref int updates, ref int valid)
        {
            if (node.type == typeof(UnityEngine.PlayerLoop.Update))
            {
                ++updates;
                if (node.subSystemList != null) foreach (var child in node.subSystemList)
                    if (child.type == typeof(RecoveryHook) && child.updateDelegate == (PlayerLoopSystem.UpdateFunction)RecoveryUpdate
                        && child.updateFunction == IntPtr.Zero && child.loopConditionFunction == IntPtr.Zero && child.subSystemList == null) ++valid;
            }
            if (node.subSystemList != null) foreach (var child in node.subSystemList) InspectRecovery(child, ref updates, ref valid);
        }
        private static void RemoveRecovery(ref PlayerLoopSystem node)
        {
            if (node.subSystemList == null) return;
            var children = new List<PlayerLoopSystem>(node.subSystemList.Length);
            foreach (var original in node.subSystemList)
            {
                if (original.type == typeof(RecoveryHook)) continue;
                var child = original; RemoveRecovery(ref child); children.Add(child);
            }
            node.subSystemList = children.ToArray();
        }
        private static void InsertRecovery(ref PlayerLoopSystem node, ref int branches)
        {
            if (node.type == typeof(UnityEngine.PlayerLoop.Update))
            {
                ++branches;
                var list = new List<PlayerLoopSystem>(node.subSystemList ?? Array.Empty<PlayerLoopSystem>());
                list.Add(new PlayerLoopSystem { type = typeof(RecoveryHook), updateDelegate = RecoveryUpdate });
                node.subSystemList = list.ToArray(); return;
            }
            if (node.subSystemList == null) return;
            for (int i = 0; i < node.subSystemList.Length; ++i) InsertRecovery(ref node.subSystemList[i], ref branches);
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Never discard failed work when domain reload is disabled.
            lastAutomaticFrame = lastHookFrame = -1;
            try { EnsureInstalled(); } catch (Exception error) { RecordError("RecoveryInstallation: " + error.Message); }
            Retry();
            if (PendingCount != 0) return;
            try { LocalFixedStepDriver.ReleaseActive(); LocalTimingOwner.ReleaseActive(); }
            catch (Exception error) { Debug.LogException(error); }
        }
    }
}
