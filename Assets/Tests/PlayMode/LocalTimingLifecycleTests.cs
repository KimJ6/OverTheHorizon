using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.LowLevel;
using OverTheHorizon.Player.Flight;
using OverTheHorizon.Player.Input;
using OverTheHorizon.Player.View;
using OverTheHorizon.LocalSimulation;
using OverTheHorizon.Timing;
using OverTheHorizon.Simulation;

namespace OverTheHorizon.Tests
{
    public sealed class LocalTimingLifecycleTests
    {
        private GameObject playerObject, rootObject, alternateReaderObject;
        private PlayerFlightController flight;
        private LocalSimulationBootstrap root;
        private World testWorld, originalWorld;
        private float originalFixed, originalScale;
        private void CreateSession(bool automatic = true, bool invalidSecondPlayer = false, bool secondPlayer = false)
        {
            originalFixed = Time.fixedDeltaTime; originalScale = Time.timeScale;
            originalWorld = World.DefaultGameObjectInjectionWorld;
            testWorld = new World("Local timing lifecycle test"); World.DefaultGameObjectInjectionWorld = testWorld;
            playerObject = new GameObject("TimingTestPlayer"); playerObject.SetActive(false);
            playerObject.AddComponent<Rigidbody>(); playerObject.AddComponent<PlayerInputReader>();
            playerObject.AddComponent<PlayerViewState>(); flight = playerObject.AddComponent<PlayerFlightController>();
            playerObject.SetActive(true);
            rootObject = new GameObject("TimingTestRoot"); rootObject.SetActive(false);
            root = rootObject.AddComponent<LocalSimulationBootstrap>();
            PlayerFlightController second = null;
            if (secondPlayer)
            {
                alternateReaderObject = new GameObject("SecondTimingPlayer"); alternateReaderObject.SetActive(false);
                alternateReaderObject.AddComponent<Rigidbody>(); alternateReaderObject.AddComponent<PlayerInputReader>();
                alternateReaderObject.AddComponent<PlayerViewState>(); second = alternateReaderObject.AddComponent<PlayerFlightController>();
                alternateReaderObject.SetActive(true);
            }
            SetField("players", invalidSecondPlayer ? new[] { flight, null } : secondPlayer ? new[] { flight, second } : new[] { flight });
            SetField("autoStart", automatic); SetField("pauseOnFocusLost", false);
            rootObject.SetActive(true);
        }
        private void SetField(string name, object value)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var field = typeof(LocalSimulationBootstrap).GetField(name, flags);
            if (field != null) field.SetValue(root, value);
            else typeof(LocalSimulationBootstrap).GetProperty(name, flags).SetValue(root, value);
        }
        private object GetField(string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var field = typeof(LocalSimulationBootstrap).GetField(name, flags);
            return field != null ? field.GetValue(root) : typeof(LocalSimulationBootstrap).GetProperty(name, flags).GetValue(root);
        }
        private static int GetPrivateInt(object target, string name)
            => (int)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static void InvokePrivate(object target, string name)
            => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
        private void SetBody(Rigidbody body)
            => typeof(PlayerFlightController).GetField("body", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(flight, body);
        private Rigidbody CreateAlternateBody()
        {
            alternateReaderObject = new GameObject("AlternateTimingBody");
            var body = alternateReaderObject.AddComponent<Rigidbody>(); body.useGravity = false;
            body.position = new Vector3(100, 100, 100); return body;
        }
        private void SetViewReader(PlayerInputReader reader)
            => typeof(PlayerViewState).GetField("inputReader", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(flight.ViewState, reader);
        private PlayerInputReader CreateAlternateReader()
        {
            alternateReaderObject = new GameObject("AlternateTimingReader");
            return alternateReaderObject.AddComponent<PlayerInputReader>();
        }
        private static IEnumerator Until(Func<bool> predicate)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 5;
            while (!predicate()) { if (Time.realtimeSinceStartupAsDouble > deadline) Assert.Fail("Timing lifecycle test timed out."); yield return null; }
        }
        [UnityTest] public IEnumerator PauseHoldsPoseAndCompletionThenResumeRequiresFreshInput()
        {
            CreateSession(); yield return Until(() => root.CompletedOrdinal >= 3);
            root.RequestPause(); yield return Until(() => root.State == SimulationLifecycle.Paused);
            ulong completed = root.CompletedOrdinal; Vector3 position = flight.Body.position;
            for (int i = 0; i < 10; ++i) yield return null;
            Assert.AreEqual(completed, root.CompletedOrdinal); Assert.AreEqual(position, flight.Body.position);
            Assert.IsTrue(flight.Body.isKinematic);
            root.RequestResume(); yield return Until(() => root.CompletedOrdinal >= completed + 2);
            Assert.IsFalse(flight.Body.isKinematic); Assert.AreEqual(SimulationLifecycle.Running, root.State);
        }
        [UnityTest] public IEnumerator RestartChangesGenerationAndTraceBeginsAtTickOne()
        {
            CreateSession(); yield return Until(() => root.CompletedOrdinal >= 3); ulong oldGeneration = root.Generation;
            root.RequestRestart(); yield return Until(() => root.Generation > oldGeneration && root.CompletedOrdinal >= 1);
            var first = root.Trace.Read(0); Assert.AreEqual(1u, first.Context.Tick);
            Assert.AreEqual(root.Generation, first.Context.Generation); Assert.AreEqual(0u, first.P0.StartStateTick);
        }
        [UnityTest] public IEnumerator DisabledFlightFaultsWithoutCompletingAnotherTick()
        {
            CreateSession(); yield return Until(() => root.CompletedOrdinal >= 3); ulong completed = root.CompletedOrdinal;
            LogAssert.Expect(LogType.Error, new Regex("Timing fault")); flight.enabled = false;
            yield return Until(() => root.State == SimulationLifecycle.Faulted);
            Assert.AreEqual(completed, root.CompletedOrdinal); Assert.IsTrue(flight.Body.isKinematic);
        }
        [UnityTest] public IEnumerator StopThenStartUsesNewGenerationAndOriginalPose()
        {
            CreateSession(false); yield return Until(() => root.State == SimulationLifecycle.Ready);
            Vector3 initial = flight.Body.position; root.RequestStart();
            yield return Until(() => root.CompletedOrdinal >= 3); ulong oldGeneration = root.Generation;
            root.RequestStop(); yield return Until(() => root.State == SimulationLifecycle.Stopped);
            ulong completed = root.CompletedOrdinal; Vector3 stopped = flight.Body.position;
            for (int i = 0; i < 5; ++i) yield return null;
            Assert.AreEqual(completed, root.CompletedOrdinal); Assert.AreEqual(stopped, flight.Body.position);
            Assert.IsTrue(flight.Body.isKinematic);
            root.RequestStart(); yield return Until(() => root.Generation > oldGeneration && root.CompletedOrdinal >= 1);
            var first = root.Trace.Read(0); Assert.AreEqual(1u, first.Context.Tick);
            Assert.AreEqual(initial.x, first.P0.StartPosition.x); Assert.AreEqual(initial.y, first.P0.StartPosition.y);
            Assert.AreEqual(initial.z, first.P0.StartPosition.z);
        }
        [UnityTest] public IEnumerator PartialPlayerValidationFailureCanBeRepairedWithoutPartialBindings()
        {
            LogAssert.Expect(LogType.Error, new Regex("Timing fault")); CreateSession(invalidSecondPlayer: true);
            yield return Until(() => root.State == SimulationLifecycle.Faulted);
            Assert.AreEqual(0ul, root.Generation); Assert.IsFalse(flight.IsManaged); Assert.IsFalse(flight.Body.isKinematic);
            Assert.IsNull(GetField("bindings"));
            SetField("players", new[] { flight }); root.RequestRestart();
            yield return Until(() => root.CompletedOrdinal >= 1);
            Assert.AreEqual(1ul, root.Generation); Assert.AreEqual(SimulationFaultCode.None, root.LastFault.Code);
        }
        [UnityTest] public IEnumerator StopWinsOverQueuedRestart()
        {
            CreateSession(); yield return Until(() => root.CompletedOrdinal >= 2); ulong generation = root.Generation;
            root.RequestRestart(); root.RequestStop(); yield return Until(() => root.State == SimulationLifecycle.Stopped);
            for (int i = 0; i < 5; ++i) yield return null;
            Assert.AreEqual(generation, root.Generation); Assert.AreEqual(SimulationLifecycle.Stopped, root.State);
            root.RequestStart(); yield return Until(() => root.Generation > generation && root.CompletedOrdinal >= 1);
        }
        [UnityTest] public IEnumerator ExplicitRestartStartsWhenAutoStartIsDisabled()
        {
            CreateSession(false); yield return Until(() => root.State == SimulationLifecycle.Ready);
            ulong generation = root.Generation;
            for (int i = 0; i < 3; ++i) yield return null;
            Assert.AreEqual(0ul, root.CompletedOrdinal);
            root.RequestRestart(); yield return Until(() => root.Generation > generation && root.CompletedOrdinal >= 1);
        }
        [UnityTest] public IEnumerator UnsupportedEdgeSurvivesFaultUntilExplicitSessionCancellation()
        {
            CreateSession(); yield return Until(() => root.CompletedOrdinal >= 2);
            ulong completed = root.CompletedOrdinal;
            LogAssert.Expect(LogType.Error, new Regex("Timing fault")); flight.InputReader.EnqueueGameplayEdge(1);
            yield return Until(() => root.State == SimulationLifecycle.Faulted);
            Assert.AreEqual(completed, root.CompletedOrdinal); Assert.AreEqual(1, flight.InputReader.Edges.Count);
            root.RequestRestart(); yield return Until(() => root.CompletedOrdinal >= 1 && root.State == SimulationLifecycle.Running);
            Assert.AreEqual(0, flight.InputReader.Edges.Count);
        }
        [UnityTest] public IEnumerator ExecutionResourceFailureRollsBackAndPreservesGeneration()
        {
            CreateSession(false);
            var conflict = new CombatStepExecutor(testWorld, 1, new SimulationTimingConfig(50));
            try
            {
                LogAssert.Expect(LogType.Error, new Regex("Timing fault"));
                yield return Until(() => root.State == SimulationLifecycle.Faulted);
                Assert.AreEqual(0ul, root.Generation); Assert.IsFalse(flight.IsManaged); Assert.IsFalse(flight.Body.isKinematic);
            }
            finally { conflict.Dispose(); }
            // Runtime-created publisher destruction completes at the end of the failure frame.
            yield return null;
            root.RequestRestart(); yield return Until(() => root.CompletedOrdinal >= 1);
            Assert.AreEqual(1ul, root.Generation);
        }
        [UnityTest] public IEnumerator RepeatedRestartKeepsOriginalBaselineAndOneDriver()
        {
            CreateSession(); yield return Until(() => root.CompletedOrdinal >= 2);
            var baseline = root.Trace.Read(0).P0.StartPosition;
            for (int i = 0; i < 5; ++i)
            {
                ulong generation = root.Generation; root.RequestRestart();
                yield return Until(() => root.Generation > generation && root.CompletedOrdinal >= 1);
                Assert.AreEqual(baseline, root.Trace.Read(0).P0.StartPosition);
                var driver = (LocalFixedStepDriver)GetField("driver");
                Assert.DoesNotThrow(driver.ValidateInstalled);
            }
        }
        [UnityTest] public IEnumerator ExternalScaleChangeIsNotOverwrittenByFaultOrCleanup()
        {
            CreateSession(); yield return Until(() => root.CompletedOrdinal >= 2);
            LogAssert.Expect(LogType.Error, new Regex("Timing fault")); Time.timeScale = 0.5f;
            yield return Until(() => root.State == SimulationLifecycle.Faulted);
            Assert.AreEqual(0.5f, Time.timeScale);
            UnityEngine.Object.DestroyImmediate(rootObject); rootObject = null;
            Assert.AreEqual(0.5f, Time.timeScale);
            Time.timeScale = originalScale;
        }
        [UnityTest] public IEnumerator OwnershipAcquisitionFailureCanBeExplicitlyRetried()
        {
            float outsideFixed = Time.fixedDeltaTime, outsideScale = Time.timeScale;
            using (var conflict = new LocalTimingOwner(new SimulationTimingConfig(60), 0.33333334f))
            {
                LogAssert.Expect(LogType.Error, new Regex("Timing fault")); CreateSession();
                originalFixed = outsideFixed; originalScale = outsideScale;
                yield return null;
                Assert.AreEqual(SimulationLifecycle.Faulted, root.State); Assert.AreEqual(0ul, root.Generation);
            }
            root.RequestRestart(); yield return Until(() => root.CompletedOrdinal >= 1);
            Assert.AreEqual(1ul, root.Generation);
        }
        [UnityTest] public IEnumerator CleanupFailureBlocksNewGenerationUntilExplicitRetrySucceeds()
        {
            CreateSession(); yield return Until(() => root.CompletedOrdinal >= 2);
            ulong generation = root.Generation; int attempts = 0;
            SetField("pendingPreparationRollback", (Action)(() =>
            { if (++attempts <= 2) throw new InvalidOperationException("Injected cleanup failure."); }));
            LogAssert.Expect(LogType.Error, new Regex("Timing fault")); root.RequestRestart();
            yield return Until(() => root.State == SimulationLifecycle.Faulted);
            Assert.AreEqual(generation, root.Generation); Assert.IsNotNull(root.LastCleanupError);
            for (int i = 0; i < 4; ++i) yield return null;
            Assert.AreEqual(1, attempts);
            var firstFault = root.LastFault;
            LogAssert.Expect(LogType.Error, new Regex("Timing fault")); root.RequestRestart();
            yield return null;
            Assert.AreEqual(2, attempts); Assert.AreEqual(generation, root.Generation);
            Assert.AreEqual(firstFault.Message, root.LastFault.Message);
            root.RequestRestart(); yield return Until(() => root.Generation > generation && root.CompletedOrdinal >= 1);
            Assert.AreEqual(3, attempts); Assert.IsNull(root.LastCleanupError);
        }
        [UnityTest] public IEnumerator DisableCleanupFailureKeepsOwnershipAndPlayersQuarantined()
        {
            CreateSession(); yield return Until(() => root.CompletedOrdinal >= 2);
            ulong generation = root.Generation, completed = root.CompletedOrdinal;
            object owner = GetField("owner"), bindings = GetField("bindings"); int attempts = 0;
            SetField("pendingPreparationRollback", (Action)(() =>
            { if (++attempts == 1) throw new InvalidOperationException("Injected disable cleanup failure."); }));
            LogAssert.Expect(LogType.Error, new Regex("Timing cleanup failed")); root.enabled = false;
            Vector3 position = flight.Body.position;
            Assert.AreEqual(SimulationLifecycle.Faulted, root.State);
            Assert.AreSame(owner, GetField("owner")); Assert.AreSame(bindings, GetField("bindings"));
            Assert.IsTrue(flight.IsManaged); Assert.IsTrue(flight.Body.isKinematic);
            Assert.IsFalse(flight.InputReader.GameplayInputEnabled); Assert.IsFalse(flight.ViewState.GameplayViewEnabled);
            Assert.AreEqual(0f, Time.timeScale);
            for (int i = 0; i < 4; ++i) yield return null;
            Assert.AreEqual(position, flight.Body.position); Assert.AreEqual(completed, root.CompletedOrdinal);
            Assert.AreEqual(1, attempts);
            root.enabled = true;
            for (int i = 0; i < 3; ++i) yield return null;
            Assert.AreEqual(SimulationLifecycle.Faulted, root.State); Assert.AreEqual(generation, root.Generation);
            Assert.AreEqual(1, attempts);
            root.RequestRestart(); yield return Until(() => root.Generation > generation && root.CompletedOrdinal >= 1);
            Assert.AreEqual(2, attempts); Assert.IsNull(root.LastCleanupError);
        }
        [UnityTest] public IEnumerator DisabledReaderPreservesUnconsumedEdgesUntilRestart()
        {
            CreateSession(); yield return Until(() => root.CompletedOrdinal >= 2);
            ulong completed = root.CompletedOrdinal;
            flight.InputReader.EnqueueGameplayEdge(1);
            LogAssert.Expect(LogType.Error, new Regex("Timing fault")); flight.InputReader.enabled = false;
            Assert.IsFalse(flight.InputReader.RawSnapshot.Valid); Assert.AreEqual(1, flight.InputReader.Edges.Count);
            yield return Until(() => root.State == SimulationLifecycle.Faulted);
            Assert.AreEqual(completed, root.CompletedOrdinal); Assert.AreEqual(1, flight.InputReader.Edges.Count);
            Assert.IsTrue(flight.Body.isKinematic);
            flight.InputReader.enabled = true;
            root.RequestRestart(); yield return Until(() => root.State == SimulationLifecycle.Running && root.CompletedOrdinal >= 1);
            Assert.AreEqual(0, flight.InputReader.Edges.Count);
        }
        [UnityTest] public IEnumerator MismatchedViewReaderIsRejectedBeforeRegistration()
        {
            CreateSession(false); SetViewReader(CreateAlternateReader());
            LogAssert.Expect(LogType.Error, new Regex("Timing fault"));
            yield return Until(() => root.State == SimulationLifecycle.Faulted);
            Assert.AreEqual(0ul, root.Generation); Assert.IsNull(GetField("bindings"));
            Assert.IsFalse(flight.IsManaged); Assert.IsFalse(flight.Body.isKinematic);
            SetViewReader(flight.InputReader); root.RequestRestart();
            yield return Until(() => root.CompletedOrdinal >= 1);
            Assert.AreEqual(1ul, root.Generation);
        }
        [UnityTest] public IEnumerator ChangedViewReaderIsRejectedOnStoppedSessionStart()
        {
            CreateSession(); yield return Until(() => root.CompletedOrdinal >= 2);
            root.RequestStop(); yield return Until(() => root.State == SimulationLifecycle.Stopped);
            ulong generation = root.Generation; SetViewReader(CreateAlternateReader());
            LogAssert.Expect(LogType.Error, new Regex("Timing fault")); root.RequestStart();
            yield return Until(() => root.State == SimulationLifecycle.Faulted);
            Assert.AreEqual(generation, root.Generation); Assert.IsTrue(flight.Body.isKinematic);
            SetViewReader(flight.InputReader); root.RequestRestart();
            yield return Until(() => root.Generation > generation && root.CompletedOrdinal >= 1);
        }
        [UnityTest] public IEnumerator ChangedViewReaderFaultsBeforeCompletingAnotherTick()
        {
            CreateSession(); var otherReader = CreateAlternateReader();
            yield return Until(() => root.CompletedOrdinal >= 2);
            ulong completed = root.CompletedOrdinal;
            // Readers updating together may have equal sequence counters; identity must still be checked.
            Assert.AreEqual(flight.InputReader.RawSnapshot.Sequence, otherReader.RawSnapshot.Sequence);
            LogAssert.Expect(LogType.Error, new Regex("Timing fault")); SetViewReader(otherReader);
            yield return Until(() => root.State == SimulationLifecycle.Faulted);
            Assert.AreEqual(completed, root.CompletedOrdinal); Assert.IsTrue(flight.Body.isKinematic);
            Assert.IsFalse(flight.GetComponent<PlayerControlSnapshotPublisher>().Latest.Valid);
        }
        [UnityTest] public IEnumerator SecondPlayerInputFailureDoesNotReserveOrReleaseTheFirstPlayer()
        {
            CreateSession(false, secondPlayer: true); yield return Until(() => root.State == SimulationLifecycle.Ready);
            var second = alternateReaderObject.GetComponent<PlayerFlightController>();
            second.InputReader.EnqueueGameplayEdge(1);
            InvokePrivate(root, "Run");
            LogAssert.Expect(LogType.Error, new Regex("Timing fault")); InvokePrivate(root, "PrePhysics");
            Assert.AreEqual(SimulationLifecycle.Faulted, root.State); Assert.AreEqual(SimulationFaultCode.Input, root.LastFault.Code);
            Assert.AreEqual(0ul, ((SimulationStepContext)GetField("current")).Ordinal);
            Assert.AreEqual(0ul, root.CompletedOrdinal); Assert.IsTrue(flight.Body.isKinematic); Assert.IsTrue(second.Body.isKinematic);
            Assert.AreEqual(1, second.InputReader.Edges.Count);
        }
        [UnityTest] public IEnumerator ChangedBodyBeforePreHookFaultsAndCanRestartWithOriginalBinding()
        {
            CreateSession(); yield return Until(() => root.CompletedOrdinal >= 2);
            ulong completed = root.CompletedOrdinal, generation = root.Generation;
            var registered = flight.Body; var replacement = CreateAlternateBody(); var position = replacement.position;
            SetBody(replacement); LogAssert.Expect(LogType.Error, new Regex("Timing fault"));
            InvokePrivate(root, "PrePhysics");
            Assert.AreEqual(SimulationLifecycle.Faulted, root.State); Assert.AreEqual(completed, root.CompletedOrdinal);
            Assert.IsTrue(registered.isKinematic); Assert.IsFalse(replacement.isKinematic);
            Assert.AreEqual(position, replacement.position); Assert.AreEqual(Vector3.zero, replacement.linearVelocity);
            SetBody(registered); root.RequestRestart();
            yield return Until(() => root.Generation > generation && root.CompletedOrdinal >= 1);
        }
        [UnityTest] public IEnumerator ChangedBodyBetweenPreHookAndFlightCannotReceiveFlightCommands()
        {
            CreateSession(); yield return Until(() => root.CompletedOrdinal >= 2);
            ulong completed = root.CompletedOrdinal; var registered = flight.Body; var replacement = CreateAlternateBody();
            InvokePrivate(root, "PrePhysics"); SetBody(replacement);
            LogAssert.Expect(LogType.Error, new Regex("Timing fault")); InvokePrivate(flight, "FixedUpdate");
            Assert.AreEqual(SimulationLifecycle.Faulted, root.State); Assert.AreEqual(completed, root.CompletedOrdinal);
            Assert.AreEqual(Vector3.zero, replacement.linearVelocity); Assert.IsTrue(registered.isKinematic);
            SetBody(registered);
        }
        [UnityTest] public IEnumerator ChangedBodyBetweenFlightAndPostHookCannotPublishCompletion()
        {
            CreateSession(); yield return Until(() => root.CompletedOrdinal >= 2);
            ulong completed = root.CompletedOrdinal; var registered = flight.Body; var replacement = CreateAlternateBody();
            var executor = (CombatStepExecutor)GetField("executor");
            InvokePrivate(root, "PrePhysics"); InvokePrivate(flight, "FixedUpdate"); SetBody(replacement);
            LogAssert.Expect(LogType.Error, new Regex("Timing fault")); InvokePrivate(root, "PostPhysics");
            Assert.AreEqual(SimulationLifecycle.Faulted, root.State); Assert.AreEqual(completed, root.CompletedOrdinal);
            Assert.AreEqual(completed, testWorld.EntityManager.GetComponentData<SimulationStepHeader>(executor.SessionEntity).CompletedOrdinal);
            Assert.AreEqual(Vector3.zero, replacement.linearVelocity); Assert.IsTrue(registered.isKinematic);
            SetBody(registered);
        }
        [UnityTest] public IEnumerator DisabledReaderAtReadyIsAConfigurationFaultRatherThanInputWait()
        {
            CreateSession(false); yield return Until(() => root.State == SimulationLifecycle.Ready);
            ulong generation = root.Generation;
            LogAssert.Expect(LogType.Error, new Regex("Timing fault")); flight.InputReader.enabled = false;
            yield return Until(() => root.State == SimulationLifecycle.Faulted);
            Assert.AreEqual(SimulationFaultCode.Configuration, root.LastFault.Code);
            Assert.AreEqual(0ul, root.CompletedOrdinal); Assert.AreEqual(generation, root.Generation);
            flight.InputReader.enabled = true; root.RequestRestart();
            yield return Until(() => root.Generation > generation && root.CompletedOrdinal >= 1);
        }
        [UnityTest] public IEnumerator ChangedViewReaderAtPausedFaultsBeforeResumeArmsInput()
        {
            CreateSession(); yield return Until(() => root.CompletedOrdinal >= 2);
            root.RequestPause(); yield return Until(() => root.State == SimulationLifecycle.Paused);
            ulong completed = root.CompletedOrdinal;
            LogAssert.Expect(LogType.Error, new Regex("Timing fault")); SetViewReader(CreateAlternateReader()); root.RequestResume();
            yield return Until(() => root.State == SimulationLifecycle.Faulted);
            Assert.AreEqual(SimulationFaultCode.Configuration, root.LastFault.Code); Assert.AreEqual(completed, root.CompletedOrdinal);
            Assert.IsFalse(flight.InputReader.GameplayInputEnabled); Assert.IsFalse(flight.ViewState.GameplayViewEnabled);
            SetViewReader(flight.InputReader);
        }
        [UnityTest] public IEnumerator DestroyedRootHandsCleanupToUpdateRecoveryWhileTimeIsZero()
        {
            CreateSession(); yield return Until(() => root.CompletedOrdinal >= 2);
            int attempts = 0;
            SetField("pendingPreparationRollback", (Action)(() =>
            { if (++attempts <= 2) throw new InvalidOperationException("Injected destruction cleanup failure."); }));
            LogAssert.Expect(LogType.Error, new Regex("Timing cleanup failed"));
            LogAssert.Expect(LogType.Error, new Regex("Timing cleanup failed"));
            UnityEngine.Object.DestroyImmediate(rootObject); rootObject = null;
            Assert.AreEqual(2, attempts); Assert.AreEqual(1, LocalSimulationBootstrap.PendingCleanupCount);
            Assert.AreEqual(0f, Time.timeScale); Assert.IsTrue(flight.IsManaged); Assert.IsTrue(flight.Body.isKinematic);
            yield return Until(() => LocalSimulationBootstrap.PendingCleanupCount == 0);
            Assert.AreEqual(3, attempts); Assert.IsFalse(flight.IsManaged); Assert.IsFalse(flight.Body.isKinematic);
            Assert.AreEqual(originalScale, Time.timeScale);
            using (var next = new LocalTimingOwner(new SimulationTimingConfig(50), 0.33333334f)) next.Validate();
        }
        [UnityTest] public IEnumerator ReorderedPostCannotCompleteAnotherActualFixedStep()
        {
            CreateSession(); yield return Until(() => root.CompletedOrdinal >= 2);
            ulong completed = root.CompletedOrdinal;
            var saved = PlayerLoop.GetCurrentPlayerLoop();
            try
            {
                var loop = PlayerLoop.GetCurrentPlayerLoop(); MovePostBeforePhysics(ref loop); PlayerLoop.SetPlayerLoop(loop);
                LogAssert.Expect(LogType.Error, new Regex("Timing fault \\[HookOrder\\]"));
                yield return Until(() => root.State == SimulationLifecycle.Faulted);
                Assert.AreEqual(completed, root.CompletedOrdinal);
                var executor = (CombatStepExecutor)GetField("executor");
                Assert.AreEqual(completed, executor.CompletedOrdinal);
                Assert.AreEqual(0, flight.InputReader.Edges.Count);
            }
            finally { PlayerLoop.SetPlayerLoop(saved); }
        }
        [UnityTest] public IEnumerator ReadyAndPausedDoNotQueryPlayerLoopEveryFrame()
        {
            CreateSession(false); yield return Until(() => root.State == SimulationLifecycle.Ready);
            var driver = (LocalFixedStepDriver)GetField("driver"); int queries = GetPrivateInt(driver, "structureValidationCount");
            for (int i = 0; i < 5; ++i) yield return null;
            Assert.AreEqual(queries, GetPrivateInt(driver, "structureValidationCount"));
            root.RequestStart(); yield return Until(() => root.CompletedOrdinal >= 2);
            root.RequestPause(); yield return Until(() => root.State == SimulationLifecycle.Paused);
            queries = GetPrivateInt(driver, "structureValidationCount");
            for (int i = 0; i < 5; ++i) yield return null;
            Assert.AreEqual(queries, GetPrivateInt(driver, "structureValidationCount"));
        }
        [UnityTest] public IEnumerator InvalidPausedLoopCannotResume()
        {
            CreateSession(); yield return Until(() => root.CompletedOrdinal >= 2);
            root.RequestPause(); yield return Until(() => root.State == SimulationLifecycle.Paused);
            ulong completed = root.CompletedOrdinal; var saved = PlayerLoop.GetCurrentPlayerLoop();
            try
            {
                var loop = PlayerLoop.GetCurrentPlayerLoop(); MovePostBeforePhysics(ref loop); PlayerLoop.SetPlayerLoop(loop);
                LogAssert.Expect(LogType.Error, new Regex("Timing fault")); root.RequestResume();
                yield return Until(() => root.State == SimulationLifecycle.Faulted);
                Assert.AreEqual(completed, root.CompletedOrdinal); Assert.IsTrue(flight.Body.isKinematic);
            }
            finally { PlayerLoop.SetPlayerLoop(saved); }
        }
        [UnityTest] public IEnumerator RemovedRecoveryHookAfterHandoffStillDrainsWithoutNewRoot()
        {
            CreateSession(); yield return Until(() => root.CompletedOrdinal >= 2);
            bool fail = true; int attempts = 0;
            SetField("pendingPreparationRollback", (Action)(() => { ++attempts; if (fail) throw new InvalidOperationException("Retained cleanup."); }));
            LogAssert.Expect(LogType.Error, new Regex("Timing cleanup failed"));
            LogAssert.Expect(LogType.Error, new Regex("Timing cleanup failed"));
            UnityEngine.Object.DestroyImmediate(rootObject); rootObject = null;
            Assert.AreEqual(1, LocalSimulationBootstrap.PendingCleanupCount);
            var loop = PlayerLoop.GetCurrentPlayerLoop(); RemoveRecovery(ref loop); PlayerLoop.SetPlayerLoop(loop);
            fail = false;
            yield return Until(() => LocalSimulationBootstrap.PendingCleanupCount == 0);
            Assert.AreEqual(3, attempts); Assert.IsFalse(flight.IsManaged); Assert.AreEqual(originalScale, Time.timeScale);
        }
        private static void MovePostBeforePhysics(ref PlayerLoopSystem node)
        {
            if (node.type == typeof(UnityEngine.PlayerLoop.FixedUpdate))
            {
                var list = new List<PlayerLoopSystem>(node.subSystemList);
                int post = list.FindIndex(x => x.type.Name == "PostTimingHook"); var hook = list[post]; list.RemoveAt(post);
                int physics = list.FindIndex(x => x.type == typeof(UnityEngine.PlayerLoop.FixedUpdate.PhysicsFixedUpdate));
                list.Insert(physics, hook); node.subSystemList = list.ToArray(); return;
            }
            if (node.subSystemList != null) for (int i = 0; i < node.subSystemList.Length; ++i) MovePostBeforePhysics(ref node.subSystemList[i]);
        }
        private static void RemoveRecovery(ref PlayerLoopSystem node)
        {
            if (node.subSystemList == null) return;
            var list = new List<PlayerLoopSystem>();
            foreach (var original in node.subSystemList)
            {
                if (original.type.Name == "RecoveryHook") continue;
                var child = original; RemoveRecovery(ref child); list.Add(child);
            }
            node.subSystemList = list.ToArray();
        }
        [UnityTest] public IEnumerator PersistentDestroyedCleanupRetainsResourcesAndRejectsNewSession()
        {
            CreateSession(); yield return Until(() => root.CompletedOrdinal >= 2);
            bool fail = true;
            SetField("pendingPreparationRollback", (Action)(() =>
            { if (fail) throw new InvalidOperationException("Injected persistent cleanup failure."); }));
            LogAssert.Expect(LogType.Error, new Regex("Timing cleanup failed"));
            LogAssert.Expect(LogType.Error, new Regex("Timing cleanup failed"));
            UnityEngine.Object.DestroyImmediate(rootObject); rootObject = null;
            try
            {
                LogAssert.Expect(LogType.Error, new Regex("Timing recovery pending")); LocalSimulationBootstrap.RetryPendingCleanup();
                Assert.AreEqual(1, LocalSimulationBootstrap.PendingCleanupCount); Assert.IsNotNull(LocalSimulationBootstrap.PendingCleanupError);
                Assert.IsTrue(flight.IsManaged); Assert.IsTrue(flight.Body.isKinematic); Assert.AreEqual(0f, Time.timeScale);
                rootObject = new GameObject("BlockedTimingRoot"); rootObject.SetActive(false);
                root = rootObject.AddComponent<LocalSimulationBootstrap>(); SetField("players", new[] { flight }); SetField("pauseOnFocusLost", false);
                LogAssert.Expect(LogType.Error, new Regex("Timing fault")); rootObject.SetActive(true);
                Assert.AreEqual(SimulationLifecycle.Faulted, root.State); Assert.AreEqual(0ul, root.Generation);
                UnityEngine.Object.DestroyImmediate(rootObject); rootObject = null;
            }
            finally { fail = false; LocalSimulationBootstrap.RetryPendingCleanup(); }
            Assert.AreEqual(0, LocalSimulationBootstrap.PendingCleanupCount); Assert.IsFalse(flight.IsManaged);
            yield return null;
        }
        [UnityTest] public IEnumerator DestructionInsideCombatUpdateDefersReleaseUntilExecutionReturns()
        {
            CreateSession(false);
            bool observedExecuting = false;
            root.ConfigureExecution += executor =>
            {
                var destroyer = executor.RegisterStage<DestroyTimingRootSystem>();
                destroyer.DestroyRoot = () =>
                {
                    observedExecuting = executor.IsExecuting;
                    UnityEngine.Object.DestroyImmediate(rootObject); rootObject = null;
                    Assert.IsTrue(executor.IsExecuting); Assert.IsTrue(testWorld.EntityManager.Exists(executor.SessionEntity));
                };
            };
            yield return Until(() => root.State == SimulationLifecycle.Ready);
            LogAssert.Expect(LogType.Error, new Regex("Timing cleanup failed"));
            LogAssert.Expect(LogType.Error, new Regex("Timing cleanup failed"));
            root.RequestStart();
            yield return Until(() => observedExecuting && LocalSimulationBootstrap.PendingCleanupCount == 0);
            Assert.IsFalse(flight.IsManaged); Assert.IsFalse(flight.Body.isKinematic); Assert.AreEqual(originalScale, Time.timeScale);
        }
        [TestCase(50)] [TestCase(60)] [TestCase(120)]
        public void TimingOwnerUsesNativeReadbackAndRestoresGlobals(int rate)
        {
            float beforeFixed = Time.fixedDeltaTime, beforeMaximum = Time.maximumDeltaTime, beforeScale = Time.timeScale;
            var owner = new LocalTimingOwner(new SimulationTimingConfig(rate), 0.33333334f);
            try
            {
                Assert.DoesNotThrow(owner.Validate); owner.SetPaused(false); Assert.DoesNotThrow(owner.Validate);
                owner.SetPaused(true); Assert.DoesNotThrow(owner.Validate);
            }
            finally { owner.Dispose(); }
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(beforeFixed).Within(beforeFixed * 1e-6f)); Assert.AreEqual(beforeMaximum, Time.maximumDeltaTime);
            Assert.AreEqual(beforeScale, Time.timeScale); Assert.DoesNotThrow(owner.Dispose);
        }
        [Test] public void TimingOwnerLeavesExternallyChangedFixedStepUntouched()
        {
            float beforeFixed = Time.fixedDeltaTime;
            var owner = new LocalTimingOwner(new SimulationTimingConfig(50), 0.33333334f);
            try
            {
                Time.fixedDeltaTime = 0.01f; float external = Time.fixedDeltaTime;
                Assert.Throws<InvalidOperationException>(owner.Validate); owner.Dispose();
                Assert.AreEqual(external, Time.fixedDeltaTime);
            }
            finally { owner.Dispose(); Time.fixedDeltaTime = beforeFixed; }
        }
        [Test] public void RepeatedOwnershipDoesNotAccumulateFixedStepDrift()
        {
            float original = Time.fixedDeltaTime;
            for (int i = 0; i < 100; ++i)
            {
                using (var owner = new LocalTimingOwner(new SimulationTimingConfig(i % 2 == 0 ? 60 : 120), 0.33333334f))
                    owner.Validate();
                Assert.That(Time.fixedDeltaTime, Is.EqualTo(original).Within(original * 1e-6f));
            }
        }
        [UnityTearDown] public IEnumerator CleanupRestoresOwnership()
        {
            if (rootObject != null) UnityEngine.Object.DestroyImmediate(rootObject);
            if (playerObject != null) UnityEngine.Object.DestroyImmediate(playerObject);
            if (alternateReaderObject != null) UnityEngine.Object.DestroyImmediate(alternateReaderObject);
            if (testWorld != null) { World.DefaultGameObjectInjectionWorld = originalWorld; testWorld.Dispose(); testWorld = null;
                Assert.That(Time.fixedDeltaTime, Is.EqualTo(originalFixed).Within(originalFixed * 1e-6f)); Assert.AreEqual(originalScale, Time.timeScale); }
            yield return null;
        }
    }
    [DisableAutoCreation]
    public partial class DestroyTimingRootSystem : CombatStepSystem
    {
        public Action DestroyRoot;
        protected override void OnStep()
        {
            var callback = DestroyRoot; DestroyRoot = null; callback?.Invoke();
        }
    }
}
