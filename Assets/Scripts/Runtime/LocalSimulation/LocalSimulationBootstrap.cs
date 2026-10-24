using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using OverTheHorizon.Player.Flight;
using OverTheHorizon.Player.Input;
using OverTheHorizon.Player.View;
using OverTheHorizon.Simulation;
using OverTheHorizon.Timing;

namespace OverTheHorizon.LocalSimulation
{
    /// <summary>Single local-session owner. Player array order defines stable IDs 1..4 for this session.</summary>
    [DefaultExecutionOrder(-1000), DisallowMultipleComponent]
    public sealed class LocalSimulationBootstrap : MonoBehaviour
    {
        [SerializeField] private SimulationTimingSettings timingSettings;
        [SerializeField] private PlayerFlightController[] players = Array.Empty<PlayerFlightController>();
        [SerializeField] private PlayerCameraController[] cameras = Array.Empty<PlayerCameraController>();
        [SerializeField] private bool autoStart = true;
        [SerializeField] private bool pauseOnFocusLost = true;
        private sealed class Binding
        {
            public uint Id;
            public PlayerFlightController Flight;
            public PlayerInputReader Reader;
            public PlayerViewState View;
            public PlayerControlSnapshotPublisher Input;
            public PlayerCombatBoundsProvider CombatBounds;
            public PlayerPhysicsSuspension Suspension;
            public PlayerKinematicState Initial, Completed;
            public FlightRuntimeSnapshot InitialFlight;
            public PlayerControlSnapshot Accepted;
            public ulong EdgeWatermark;
            public bool CreatedPublisher, ManagedBound, OriginalInputEnabled, OriginalViewEnabled;

            // Bootstrap owns the session, while a Binding owns
            // the per-player handoff from accepted input through physics to ECS data.
            public void AcceptInputForStep(uint completedTick)
            {
                // This runs before Progress.Begin. A bad participant therefore
                // rejects the tick before any authoritative reservation exists.
                if (Flight == null || !Flight.isActiveAndEnabled || Flight.Body == null)
                    throw new InvalidOperationException("A registered Flight is disabled or destroyed.");
                Accepted = Input.Latest; EdgeWatermark = Reader.Edges.HighWatermark;
                if (Reader.Edges.Count != 0)
                    throw new InvalidOperationException("Discrete gameplay edges have no consumer in this implementation; refusing to drop or acknowledge them.");
                if (Completed.StateTick != completedTick) throw new InvalidOperationException("Start snapshot is stale.");
            }
            public void PreparePhysicsStep(SimulationStepContext context, Vector3 gravity)
            {
                // This runs only after the context is reserved. It prepares the
                // exact accepted input without changing completion state.
                Suspension.Restore();
                Flight.PrepareStep(context, Accepted, gravity);
            }
            public PlayerStepElement CaptureStepFrame(SimulationStepContext context, Vector3 gravity)
            {
                // The Flight context is the live witness that Pre ran before
                // ScriptRunBehaviourFixedUpdate;
                // a reordered pre hook faults instead of publishing stale physics.
                // Capture is observational; ECS validation still decides success.
                if (Flight.AppliedGeneration != context.Generation || Flight.AppliedOrdinal != context.Ordinal)
                    throw new InvalidOperationException("A mandatory Flight step did not complete.");
                return LocalPlayerStatePublisher.BuildStepFrame(Id, Completed, Accepted, EdgeWatermark, context, gravity, Suspension.Body);
            }
            public void PublishStepFrame(PlayerStepElement frame)
            {
                // The caller invokes this only after the authoritative commit.
                // Advancing a local snapshot or input watermark is never a commit.
                Completed = LocalPlayerStatePublisher.ReadCompleted(frame);
                Reader.Edges.AckThrough(EdgeWatermark);
            }
        }
        private sealed class BindingFault : InvalidOperationException
        {
            public readonly uint PlayerId;
            public BindingFault(uint id, string message) : base("Player " + id + ": " + message) { PlayerId = id; }
        }
        private sealed class PreparationRollback
        {
            public Binding[] Candidates;
            public bool InitialRegistration;
            public PlayerFlightController.PreparationState[] Flight;
            public PlayerViewState.PreparationState[] View;
            public PlayerKinematicState[] Body, Completed;
        }
        private sealed class SessionResources : LocalCleanupRecovery.IWork
        {
            public Binding[] Bindings;
            public LocalTimingOwner Owner;
            public LocalFixedStepDriver Driver;
            public CombatStepExecutor Executor;
            public NativeArray<PlayerStepElement> Frame;
            public PreparationRollback Rollback;
            // Failure injection used by lifecycle regression tests; production rollback is typed data.
            public Action PendingPreparationRollback;
            public string CleanupStage { get; private set; }
            public void Suspend(bool cancelEvents)
            {
                var errors = new List<Exception>();
                Suspend(Bindings, cancelEvents, errors);
                if (Rollback != null && Rollback.Candidates != Bindings) Suspend(Rollback.Candidates, cancelEvents, errors);
                if (errors.Count != 0) throw new AggregateException("Player quarantine failed.", errors);
            }
            private static void Suspend(Binding[] targets, bool cancelEvents, List<Exception> errors)
            {
                if (targets == null) return;
                foreach (var b in targets) if (b != null)
                {
                    Attempt(() => { if (b.Flight != null) b.Flight.SetManagedExecutionEnabled(false); }, errors);
                    Attempt(() => b.Suspension?.Freeze(), errors);
                    Attempt(() => { if (b.Reader != null) { b.Reader.SetGameplayInputEnabled(false); if (cancelEvents) b.Reader.ResetInputCache(); } }, errors);
                    Attempt(() => { if (b.View != null) b.View.SetGameplayViewEnabled(false); }, errors);
                    Attempt(() => { if (b.Input != null) b.Input.SetPublishing(false); }, errors);
                }
            }
            public void Hold()
            {
                if (Bindings != null) foreach (var b in Bindings) b?.Suspension?.Hold();
                if (Rollback != null && Rollback.Candidates != Bindings)
                    foreach (var b in Rollback.Candidates) b?.Suspension?.Hold();
            }
            public void DetachCallbacks()
            {
                Driver?.Deactivate();
                Detach(Bindings);
                if (Rollback != null) Detach(Rollback.Candidates);
            }
            private static void Detach(Binding[] targets)
            { if (targets != null) foreach (var b in targets) if (b?.Flight != null) b.Flight.DetachManagedFailureSink(); }
            public void ReleaseExecution()
            {
                CleanupStage = "Execution";
                Driver?.Deactivate();
                if (Executor != null && Executor.IsExecuting)
                    throw new InvalidOperationException("Combat execution must return before cleanup.");
                // Do not free backing storage if hook removal fails.
                Driver?.Dispose(); Driver = null;
                Executor?.Dispose(); Executor = null;
                if (Frame.IsCreated) Frame.Dispose(); Frame = default;
            }
            public void FinishRollback()
            {
                CleanupStage = "PreparationRollback";
                if (PendingPreparationRollback != null) { PendingPreparationRollback(); PendingPreparationRollback = null; }
                if (Rollback == null) return;
                var r = Rollback;
                var errors = new List<Exception>();
                for (int i = r.Candidates.Length - 1; i >= 0; --i)
                {
                    var b = r.Candidates[i]; int slot = i;
                    Attempt(() => { if (b.Input != null) b.Input.Unbind(); }, errors);
                    Attempt(() =>
                    {
                        if (b.Flight == null) return;
                        if (r.InitialRegistration && b.ManagedBound) b.Flight.UnbindManaged();
                        b.Flight.RestorePreparationState(r.Flight[slot]); b.ManagedBound = !r.InitialRegistration;
                    }, errors);
                    Attempt(() => { if (b.View != null) b.View.RestorePreparationState(r.View[slot]); }, errors);
                    Attempt(() =>
                    {
                        if (b.Suspension == null) return;
                        var state = r.Body[slot];
                        b.Suspension.SetResumeState(state.Position, state.Rotation, state.Velocity, state.AngularVelocity);
                        if (r.InitialRegistration) b.Suspension.Restore();
                    }, errors);
                    b.Completed = r.Completed[i];
                    Attempt(() => { if (b.Reader != null) b.Reader.SetGameplayInputEnabled(r.InitialRegistration && b.OriginalInputEnabled); }, errors);
                    if (!r.InitialRegistration) Attempt(() => { if (b.View != null) b.View.SetGameplayViewEnabled(false); }, errors);
                    if (r.InitialRegistration && b.CreatedPublisher && b.Input != null)
                        Attempt(() => { b.Input.enabled = false; UnityEngine.Object.Destroy(b.Input); b.Input = null; }, errors);
                }
                if (errors.Count != 0) throw new AggregateException("Preparation rollback failed.", errors);
                Rollback = null;
            }
            public void Cleanup()
            {
                Executor?.Invalidate("Session cleanup requested.");
                CleanupStage = "Quarantine";
                Suspend(true); Owner?.PauseIfOwned(); Driver?.Deactivate();
                if (Driver != null && Driver.IsExecuting)
                    throw new InvalidOperationException("A timing callback must return before final cleanup.");
                ReleaseExecution(); FinishRollback();
                CleanupStage = "Participants";
                var errors = new List<Exception>();
                if (Bindings != null) foreach (var b in Bindings) if (b != null)
                {
                    Attempt(() => { if (b.Reader != null) { b.Reader.ResetInputCache(); b.Reader.SetGameplayInputEnabled(b.OriginalInputEnabled); } }, errors);
                    Attempt(() => { if (b.View != null) b.View.SetGameplayViewEnabled(b.OriginalViewEnabled); }, errors);
                    Attempt(() => b.Suspension?.Restore(), errors);
                }
                if (errors.Count != 0)
                {
                    Attempt(() => Suspend(false), errors);
                    throw new AggregateException("Participant restoration failed.", errors);
                }
                if (Bindings != null) foreach (var b in Bindings) if (b != null)
                {
                    Attempt(() => { if (b.Input != null) b.Input.Unbind(); }, errors);
                    Attempt(() => { if (b.Flight != null) b.Flight.UnbindManaged(); }, errors);
                    if (b.CreatedPublisher && b.Input != null)
                        Attempt(() => { b.Input.enabled = false; UnityEngine.Object.Destroy(b.Input); b.Input = null; }, errors);
                }
                if (errors.Count != 0) throw new AggregateException("Participant release failed.", errors);
                Bindings = null;
                CleanupStage = "TimingOwner";
                Owner?.Dispose(); Owner = null;
                CleanupStage = "Complete";
            }
        }
        private SessionResources resources = new SessionResources();
        private Binding[] bindings { get => resources.Bindings; set => resources.Bindings = value; }
        private LocalTimingOwner owner { get => resources.Owner; set => resources.Owner = value; }
        private LocalFixedStepDriver driver { get => resources.Driver; set => resources.Driver = value; }
        private CombatStepExecutor executor { get => resources.Executor; set => resources.Executor = value; }
        private ref NativeArray<PlayerStepElement> frame => ref resources.Frame;
        private SimulationTimingConfig config;
        private readonly SimulationProgress progress = new SimulationProgress();
        private SimulationStepContext current;
        private Vector3 stepGravity;
        private ulong generation;
        private bool started, prepared, resumePending, startPending, pausePending, restartPending, stopPending;
        private int traceCapacity;
        private bool resumeArmed;
        private Action pendingPreparationRollback { get => resources.PendingPreparationRollback; set => resources.PendingPreparationRollback = value; }
        private SimulationFaultCode stage;
        public SimulationLifecycle State { get; private set; }
        public SimulationFault LastFault { get; private set; }
        public ulong Generation => progress.Generation;
        public uint CompletedTick => progress.CompletedTick;
        public ulong CompletedOrdinal => progress.CompletedOrdinal;
        public SimulationTraceBuffer Trace { get; private set; }
        public string LastCleanupError { get; private set; }
        public static int PendingCleanupCount => LocalCleanupRecovery.PendingCount;
        public static string PendingCleanupError => LocalCleanupRecovery.LastError;
        public static string PendingCleanupStage => LocalCleanupRecovery.Stage;
        public static void RetryPendingCleanup() { LocalCleanupRecovery.Retry(); }
        // Register executor-owned stages during preparation, before the pipeline is sealed.
        public event Action<CombatStepExecutor> ConfigureExecution;

        private void Awake() { Acquire(); }
        private void OnEnable()
        { if (started && owner == null) Acquire(); }
        private void Start() { started = true; }
        private void Acquire()
        {
            try
            {
                LocalCleanupRecovery.RequireDrained();
                if (driver != null || executor != null || frame.IsCreated || pendingPreparationRollback != null || resources.Rollback != null)
                    throw new InvalidOperationException("Previous session resources have not been safely released.");
                if (timingSettings != null) timingSettings.Validate();
                config = timingSettings != null ? timingSettings.CreateConfig() : new SimulationTimingConfig(50);
                traceCapacity = timingSettings != null ? timingSettings.TraceCapacity : 512;
                owner = new LocalTimingOwner(config, timingSettings != null ? timingSettings.MaximumDeltaTime : 0.33333334f);
                startPending = autoStart;
                State = SimulationLifecycle.Preparing;
            }
            catch (Exception error) { Fail(SimulationFaultCode.Ownership, error.Message); }
        }
        private void Update()
        {
            if (!started) return;
            try
            {
                if (progress.StepOpen) { Fail(SimulationFaultCode.HookOrder, "A fixed step escaped without its post hook."); return; }
                if (ProcessRequests()) return;
                if (owner == null || State == SimulationLifecycle.Faulted) { HoldBodies(); return; }
                try { owner.Validate(); }
                catch (Exception error) { Fail(SimulationFaultCode.Ownership, error.Message); return; }
                if (bindings != null) ValidateRegisteredBindings();
                if (prepared)
                {
                    if (executor == null || !executor.World.IsCreated) { Fail(SimulationFaultCode.WorldLost, "Session World is unavailable."); return; }
                }
                if (State == SimulationLifecycle.Preparing)
                {
                    if (!prepared)
                    {
                        var world = World.DefaultGameObjectInjectionWorld;
                        if (world == null || !world.IsCreated) return;
                        Prepare(world);
                    }
                    if (AllInputsReady())
                    {
                        State = SimulationLifecycle.Ready;
                        if (startPending) Run();
                    }
                }
                else if (State == SimulationLifecycle.Ready && startPending && AllInputsReady()) Run();
                else if (State == SimulationLifecycle.Paused && resumePending && AllInputsReady()) Run();
                if (State != SimulationLifecycle.Running) HoldBodies();
            }
            catch (Exception error) { Fail(SimulationFaultCode.Configuration, error); }
        }
        // Invoked in Update and before/after a fixed step, never while a step is open.
        private bool ProcessRequests()
        {
            if (progress.StepOpen) return false;
            if (stopPending) { StopSession(); return true; }
            if (restartPending)
            {
                StopSession();
                if (owner != null && !owner.IsActive) { owner.Dispose(); owner = null; }
                if (owner == null) Acquire();
                else
                {
                    // Explicit recovery reacquires changed globals instead of overwriting them during Fault.
                    try { owner.Validate(); }
                    catch { owner.Dispose(); owner = null; Acquire(); }
                }
                if (owner != null && owner.IsActive) { State = SimulationLifecycle.Preparing; startPending = true; }
                return true;
            }
            if (startPending && (State == SimulationLifecycle.Stopped || State == SimulationLifecycle.Uninitialized))
            {
                if (owner == null) Acquire();
                if (owner != null && owner.IsActive) { State = SimulationLifecycle.Preparing; startPending = true; }
            }
            if (pausePending && State == SimulationLifecycle.Running)
            { Pause(); startPending = false; return true; }
            if (resumePending && State == SimulationLifecycle.Paused && !resumeArmed)
            { ValidateRegisteredBindings(); ArmInputs(bindings); resumeArmed = true; }
            return false;
        }
        private void Prepare(World world)
        {
            owner.Validate();
            if (driver != null || executor != null || frame.IsCreated || pendingPreparationRollback != null || resources.Rollback != null)
                throw new InvalidOperationException("Previous execution resources are still present.");
            bool initialRegistration = bindings == null;
            Binding[] candidates = initialRegistration ? ValidatePlayers() : bindings;
            if (!initialRegistration) ValidateRegisteredPlayers();
            ulong nextGeneration = checked(generation + 1);
            var nextTrace = new SimulationTraceBuffer(traceCapacity);
            var previousFlight = new PlayerFlightController.PreparationState[candidates.Length];
            var previousView = new PlayerViewState.PreparationState[candidates.Length];
            var previousBody = new PlayerKinematicState[candidates.Length];
            var previousCompleted = new PlayerKinematicState[candidates.Length];
            for (int i = 0; i < candidates.Length; ++i)
            {
                var b = candidates[i]; previousFlight[i] = b.Flight.CapturePreparationState();
                previousView[i] = b.View.CapturePreparationState(); previousCompleted[i] = b.Completed;
                previousBody[i] = b.Suspension != null && b.Suspension.Suspended
                    ? LocalPlayerStatePublisher.CaptureInitial(b.Suspension) : LocalPlayerStatePublisher.CaptureEnd(b.Flight.Body, 0);
            }
            try
            {
                // No registration is published until every player and resource is prepared.
                foreach (var b in candidates)
                {
                    if (initialRegistration)
                    {
                        b.Suspension = new PlayerPhysicsSuspension(b.Flight.Body); b.Suspension.Freeze();
                        b.Initial = LocalPlayerStatePublisher.CaptureInitial(b.Suspension);
                        b.InitialFlight = b.Flight.CaptureFlightState();
                        b.Input = b.Flight.GetComponent<PlayerControlSnapshotPublisher>();
                        if (b.Input == null)
                        { b.Input = b.Flight.gameObject.AddComponent<PlayerControlSnapshotPublisher>(); b.CreatedPublisher = true; }
                        b.Flight.BindManaged(b.Id, OnFlightFailure); b.ManagedBound = true;
                    }
                    b.Suspension.Freeze();
                    b.Suspension.SetResumeState(b.Initial.Position, b.Initial.Rotation, b.Initial.Velocity, b.Initial.AngularVelocity);
                    Vector3 forward = b.Initial.Rotation * Vector3.forward;
                    b.View.SetView(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg, -Mathf.Asin(Mathf.Clamp(forward.y, -1, 1)) * Mathf.Rad2Deg);
                    b.Flight.ResetFlightState(b.InitialFlight.ControlledVelocity, b.InitialFlight.GravityVelocity);
                    b.Completed = LocalPlayerStatePublisher.CaptureInitial(b.Suspension);
                    b.Input.Bind(b.Reader, b.View, nextGeneration, b.Id);
                }
                frame = new NativeArray<PlayerStepElement>(4, Allocator.Persistent);
                CombatStepExecutor retained = null;
                try { CombatStepExecutor.Create(world, nextGeneration, config, out retained); }
                finally { executor = retained; }
                executor.World.EntityManager.AddBuffer<PlayerCombatBoundsStepElement>(executor.SessionEntity);
                ConfigureExecution?.Invoke(executor);
                executor.Seal();
                driver = new LocalFixedStepDriver(PrePhysics, PostPhysics);
                driver.ValidateInstalled();
                foreach (var camera in cameras) if (camera != null) camera.SnapNow();
                foreach (var b in candidates) b.Reader.ResetInputCache();
                ArmInputs(candidates);
                progress.Reset(nextGeneration); generation = nextGeneration;
                bindings = candidates; Trace = nextTrace; current = default;
                LastFault = default; LastCleanupError = null; prepared = true;
            }
            catch (Exception error)
            {
                // Retain failed rollback work. A later Stop/Restart must finish it before allocating a new session.
                resources.Rollback = new PreparationRollback { Candidates = candidates, InitialRegistration = initialRegistration,
                    Flight = previousFlight, View = previousView, Body = previousBody, Completed = previousCompleted };
                try { FinishPreparationRollback(); }
                catch (Exception cleanup) { throw new AggregateException("Session preparation and rollback failed.", error, cleanup); }
                throw;
            }
        }
        private void FinishPreparationRollback()
        {
            ReleaseExecution();
            resources.FinishRollback();
        }
        private Binding[] ValidatePlayers()
        {
            if (players == null || players.Length < 1 || players.Length > 4)
                throw new InvalidOperationException("Assign one to four Flight controllers in stable player-ID order.");
            var candidates = new Binding[players.Length];
            for (int i = 0; i < players.Length; ++i)
            {
                var flight = players[i];
                if (flight == null || !flight.isActiveAndEnabled || flight.Body == null || flight.Body.isKinematic
                    || flight.InputReader == null || !flight.InputReader.isActiveAndEnabled
                    || flight.ViewState == null || !flight.ViewState.isActiveAndEnabled)
                    throw new InvalidOperationException("Players require an active Flight, dynamic Rigidbody, input and view.");
                if (flight.ViewState.InputReader != flight.InputReader)
                    throw new InvalidOperationException("Flight and View must use the same input reader.");
                flight.ValidateManagedBinding((uint)(i + 1));
                var publisher = flight.GetComponent<PlayerControlSnapshotPublisher>();
                if (publisher != null && !publisher.isActiveAndEnabled)
                    throw new InvalidOperationException("The control snapshot publisher must be active.");
                for (int j = 0; j < i; ++j)
                    if (candidates[j].Flight.Body == flight.Body || candidates[j].Reader == flight.InputReader || candidates[j].View == flight.ViewState)
                        throw new InvalidOperationException("Player body, input and view bindings must be unique.");
                candidates[i] = new Binding { Id = (uint)(i + 1), Flight = flight, Reader = flight.InputReader,
                    CombatBounds = flight.GetComponent<PlayerCombatBoundsProvider>(),
                    View = flight.ViewState, OriginalInputEnabled = flight.InputReader.GameplayInputEnabled,
                    OriginalViewEnabled = flight.ViewState.GameplayViewEnabled };
            }
            return candidates;
        }
        private void ValidateRegisteredPlayers()
        {
            ValidateRegisteredBindings();
            foreach (var b in bindings)
                if (!b.Suspension.Suspended || !b.Suspension.Body.isKinematic)
                    throw new InvalidOperationException("Registered players must remain suspended while preparing.");
        }
        private void ValidateRegisteredBindings()
        {
            if (players == null || players.Length != bindings.Length)
                throw new InvalidOperationException("Registered player composition cannot change within an ownership lifetime.");
            for (int i = 0; i < bindings.Length; ++i)
            {
                var b = bindings[i];
                if (b == null) throw new BindingFault((uint)(i + 1), "Registered binding is missing.");
                CheckReference(b.Id, "Flight", b.Flight, players[i]);
                if (!b.Flight.isActiveAndEnabled || !b.Flight.IsManaged) throw new BindingFault(b.Id, "Flight is inactive or unmanaged.");
                CheckReference(b.Id, "Rigidbody", b.Suspension?.Body, b.Flight.Body);
                if (b.Suspension.Body.isKinematic != b.Suspension.Suspended)
                    throw new BindingFault(b.Id, "Rigidbody suspension state was changed externally.");
                CheckReference(b.Id, "Flight.Reader", b.Reader, b.Flight.InputReader);
                CheckReference(b.Id, "Flight.View", b.View, b.Flight.ViewState);
                CheckReference(b.Id, "View.Reader", b.Reader, b.View.InputReader);
                if (!b.Reader.isActiveAndEnabled || !b.View.isActiveAndEnabled || b.Input == null || !b.Input.isActiveAndEnabled)
                    throw new BindingFault(b.Id, "Reader, View or publisher is inactive or destroyed.");
                b.Flight.ValidateManagedReferences();
                if (prepared && !b.Input.BindingMatches(b.Reader, b.View, generation, b.Id))
                    throw new BindingFault(b.Id, "Publisher binding or generation changed.");
            }
        }
        private static void CheckReference(uint id, string label, UnityEngine.Object expected, UnityEngine.Object actual)
        {
            if (expected == null || actual != expected)
                throw new BindingFault(id, label + " expected " + Describe(expected) + ", actual " + Describe(actual) + ".");
        }
        private static string Describe(UnityEngine.Object value)
            => value == null ? "null/destroyed" : value.name + " (#" + value.GetInstanceID() + ")";
        private void ArmInputs(Binding[] targets)
        {
            if (targets == null) return;
            foreach (var b in targets)
            { b.Reader.SetGameplayInputEnabled(true); b.View.SetGameplayViewEnabled(true); b.Input.SetPublishing(true); }
        }
        private void DisarmInputs(bool cancelEvents = false)
        {
            if (bindings == null) return;
            var errors = new List<Exception>();
            foreach (var b in bindings) if (b != null)
            {
                Attempt(() => { if (b.Reader != null) { b.Reader.SetGameplayInputEnabled(false); if (cancelEvents) b.Reader.ResetInputCache(); } }, errors);
                Attempt(() => { if (b.View != null) b.View.SetGameplayViewEnabled(false); }, errors);
                Attempt(() => { if (b.Input != null) b.Input.SetPublishing(false); }, errors);
            }
            if (errors.Count != 0) throw new AggregateException("Input disarming failed.", errors);
        }
        private bool AllInputsReady()
        {
            if (!prepared || bindings == null) return false;
            foreach (var b in bindings)
                if (!b.Input.Latest.Valid || b.Input.Latest.Generation != generation || b.Input.Latest.PlayerId != b.Id) return false;
            return true;
        }
        private void Run()
        {
            ValidateRegisteredBindings();
            owner.Validate();
            // Installation is checked at the Ready/Paused -> Running boundary.
            // Running steps use the retained native-physics fence instead of polling
            // and traversing the PlayerLoop once per audit interval.
            try { driver.ValidateInstalled(); }
            catch (Exception error) { Fail(SimulationFaultCode.HookOrder, error); return; }
            foreach (var b in bindings) b.Flight.SetManagedExecutionEnabled(true);
            startPending = resumePending = resumeArmed = pausePending = false;
            State = SimulationLifecycle.Running; owner.SetPaused(false);
        }
        private void Pause()
        {
            if (progress.StepOpen) { pausePending = true; return; }
            SuspendBodies(); DisarmInputs(); owner.SetPaused(true);
            State = SimulationLifecycle.Paused; pausePending = resumePending = resumeArmed = false;
        }
        private void PrePhysics()
        {
            try
            {
                if (ProcessRequests() || State != SimulationLifecycle.Running) { HoldBodies(); return; }
                stage = SimulationFaultCode.HookOrder; driver.ValidateExecutionOrder();
                stage = SimulationFaultCode.Ownership; owner.Validate();
                stage = SimulationFaultCode.WorldLost;
                if (executor == null || !executor.World.IsCreated) throw new InvalidOperationException("Session World is unavailable.");
                stage = SimulationFaultCode.Configuration; ValidateRegisteredBindings();
                stage = SimulationFaultCode.Input;
                if (!AllInputsReady()) throw new InvalidOperationException("A current player input/view pair is unavailable.");
                stepGravity = Physics.gravity;
                foreach (var b in bindings)
                    b.AcceptInputForStep(progress.CompletedTick);
                stage = SimulationFaultCode.HookOrder; current = progress.Begin(config);
                for (int i = 0; i < 4; ++i) frame[i] = default;
                stage = SimulationFaultCode.Flight;
                foreach (var b in bindings) b.PreparePhysicsStep(current, stepGravity);
            }
            catch (Exception error) { Fail(stage, error); }
        }
        private void FixedUpdate()
        {
            // Detect missing/replaced pre hook even when no Flight happens to execute this tick.
            if (State == SimulationLifecycle.Running && !progress.StepOpen)
                Fail(SimulationFaultCode.HookOrder, "FixedUpdate ran without a reserved step.");
        }
        private void PostPhysics()
        {
            if (State != SimulationLifecycle.Running) { HoldBodies(); return; }
            try
            {
                stage = SimulationFaultCode.HookOrder;
                if (!progress.StepOpen) throw new InvalidOperationException("Post hook has no reserved step.");
                driver.ValidateExecutionOrder();
                stage = SimulationFaultCode.Ownership; owner.Validate();
                stage = SimulationFaultCode.Configuration; ValidateRegisteredBindings();
                stage = SimulationFaultCode.Snapshot;
                for (int i = 0; i < bindings.Length; ++i)
                    frame[i] = bindings[i].CaptureStepFrame(current, stepGravity);
                var combatBounds = executor.World.EntityManager.GetBuffer<PlayerCombatBoundsStepElement>(executor.SessionEntity);
                combatBounds.Clear();
                for (int i = 0; i < bindings.Length; ++i)
                    if (bindings[i].CombatBounds != null && bindings[i].CombatBounds.TryBuild(frame[i], out var bounds))
                        combatBounds.Add(bounds);
                stage = SimulationFaultCode.Ecs;
                executor.Execute(current, frame, progress);
                // Destruction inside group.Update quarantines the session and defers release.
                if (State != SimulationLifecycle.Running || !progress.StepOpen) return;
                stage = SimulationFaultCode.Configuration; ValidateRegisteredBindings();
                executor.ValidatePending(current, progress);
                // Validate the still-open reservation first,
                // then Progress performs the sole completion write. The executor
                // only releases its pending execution state after observing it.
                progress.Complete(current);
                executor.AcknowledgeAuthoritativeCompletion(current, progress);
                // Local player state advances only after the authoritative
                // progress and executor boundary both accepted this frame.
                for (int i = 0; i < bindings.Length; ++i)
                    bindings[i].PublishStepFrame(frame[i]);
                // A diagnostic failure after publication cannot turn a completed step into an aborted one.
                try { Trace.Append(current, true, frame); }
                catch (Exception error) { Fail(SimulationFaultCode.Capacity, "Completed step trace failed: " + error.Message); return; }
                ProcessRequests();
            }
            catch (Exception error) { Fail(stage, error); }
        }
        private void OnFlightFailure(uint id, string message) { Fail(SimulationFaultCode.Flight, message, id); }
        private void Fail(SimulationFaultCode code, Exception error)
        {
            var bindingError = error as BindingFault;
            Fail(bindingError != null ? SimulationFaultCode.Configuration : code, error.Message, bindingError?.PlayerId ?? 0);
        }
        private void Fail(SimulationFaultCode code, string message, uint playerId = 0)
        {
            if (State == SimulationLifecycle.Faulted) return;
            if (LastFault.Code == SimulationFaultCode.None)
                LastFault = new SimulationFault(code, progress.Generation, progress.ReservedTick, progress.CompletedTick, code.ToString(), message, playerId);
            bool hadStep = progress.StepOpen;
            executor?.Invalidate(message);
            progress.Abort(); ClearRequests(); State = SimulationLifecycle.Faulted;
            var errors = new List<Exception>();
            Attempt(SuspendBodies, errors); Attempt(() => DisarmInputs(), errors);
            Attempt(() => owner?.PauseIfOwned(), errors);
            if (hadStep && frame.IsCreated && Trace != null) Attempt(() => Trace.Append(current, false, frame), errors);
            if (errors.Count != 0) LastCleanupError = new AggregateException(errors).Message;
            Debug.LogError("Timing fault [" + code + "]: " + message, this);
        }
        private void SuspendBodies()
        {
            if (bindings == null) return;
            var errors = new List<Exception>();
            foreach (var b in bindings) if (b != null)
            {
                Attempt(() => { if (b.Flight != null) b.Flight.SetManagedExecutionEnabled(false); }, errors);
                Attempt(() => b.Suspension?.Freeze(), errors);
            }
            if (errors.Count != 0) throw new AggregateException("Player suspension failed.", errors);
        }
        private void HoldBodies()
        { if (bindings != null) foreach (var b in bindings) if (b != null) b.Suspension.Hold(); }
        private void ClearRequests()
        { resumePending = resumeArmed = startPending = pausePending = restartPending = stopPending = false; }
        private static void Attempt(Action action, List<Exception> errors)
        { try { action(); } catch (Exception error) { errors.Add(error); } }
        private void ReleaseExecution()
        {
            prepared = false;
            try { resources.ReleaseExecution(); }
            catch (Exception error) { LastCleanupError = error.Message; throw; }
        }
        private void StopSession()
        {
            ClearRequests(); State = SimulationLifecycle.Stopping;
            var errors = new List<Exception>();
            Attempt(SuspendBodies, errors); Attempt(() => DisarmInputs(true), errors);
            Attempt(() => owner?.PauseIfOwned(), errors);
            progress.Abort(); Attempt(ReleaseExecution, errors);
            if (driver == null && executor == null && !frame.IsCreated)
                Attempt(FinishPreparationRollback, errors);
            if (errors.Count != 0)
            { LastCleanupError = new AggregateException(errors).Message; throw new AggregateException("Session stop failed.", errors); }
            State = SimulationLifecycle.Stopped;
        }
        [ContextMenu("Timing/Start")]
        public void RequestStart()
        {
            if (!isActiveAndEnabled) return;
            if (State == SimulationLifecycle.Ready || State == SimulationLifecycle.Preparing
                || State == SimulationLifecycle.Stopped || State == SimulationLifecycle.Uninitialized) startPending = true;
        }
        [ContextMenu("Timing/Pause")]
        public void RequestPause() { if (State == SimulationLifecycle.Running) pausePending = true; }
        [ContextMenu("Timing/Resume")]
        public void RequestResume()
        { if (State == SimulationLifecycle.Paused) resumePending = true; }
        [ContextMenu("Timing/Restart")]
        public void RequestRestart() { if (isActiveAndEnabled) restartPending = true; }
        [ContextMenu("Timing/Stop")]
        public void RequestStop() { if (isActiveAndEnabled) stopPending = true; }
        [ContextMenu("Timing/Export Trace CSV")]
        public void ExportTraceCsv()
        { if (Trace != null) Trace.ExportCsv(System.IO.Path.Combine(Application.persistentDataPath, "horizon-timing.csv")); }
        [ContextMenu("Timing/Export Replay Binary")]
        public void ExportReplayBinary()
        { if (Trace != null) SimulationReplayStepSource.Export(Trace, System.IO.Path.Combine(Application.persistentDataPath, "horizon-timing.htz")); }
        private void OnApplicationFocus(bool focus) { if (!focus && pauseOnFocusLost) RequestPause(); }
        private void OnApplicationPause(bool pause) { if (pause) RequestPause(); }
        private void OnDisable() { Cleanup(); }
        private void OnDestroy()
        {
            Cleanup();
            if (State != SimulationLifecycle.Uninitialized)
            {
                resources.DetachCallbacks();
                LocalCleanupRecovery.Retain(resources);
                resources = new SessionResources();
            }
        }
        private void Cleanup()
        {
            ClearRequests(); progress.Abort(); prepared = false;
            try
            {
                resources.Cleanup();
                State = SimulationLifecycle.Uninitialized; LastCleanupError = null;
            }
            catch (Exception error) { ReportCleanupFailure(new List<Exception> { error }); }
        }
        private void ReportCleanupFailure(List<Exception> errors)
        {
            LastCleanupError = new AggregateException(errors).Message;
            State = SimulationLifecycle.Faulted;
            Debug.LogError("Timing cleanup failed: " + LastCleanupError, this);
        }
    }
}
