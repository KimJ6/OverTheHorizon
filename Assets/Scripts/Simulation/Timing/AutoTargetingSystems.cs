using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace OverTheHorizon.Simulation
{
    // P1-B's first real combat candidate. StableId is gameplay identity, never an Entity index or pool slot.
    public struct CombatTargetCandidate : IComponentData
    {
        public ulong StableId;
        public float3 Anchor;
        public byte Targetable;
    }
    public struct CombatTargetEvaluation : IComponentData
    {
        public ulong Generation, Ordinal, StableId;
        public uint Tick;
        public byte Eligible;
        public float DistanceSq, ViewDot;
    }
    public struct CombatTargetAssignment : IComponentData
    {
        public ulong Generation, Ordinal, TargetStableId;
        public uint Tick, PlayerId, WeaponId;
        public byte HasTarget;
    }

    [DisableAutoCreation]
    [UpdateAfter(typeof(SimulationEntrySystem))]
    [UpdateBefore(typeof(SimulationCompleteSystem))]
    public partial class AutoTargetingProducerSystem : CombatStepSystem
    {
        private EntityQuery candidates;
        // Prototype values only. SYS-120 deliberately leaves final tuning open.
        public uint PlayerId = 1, WeaponId = 1;
        public float MaxRange = 100f, MinimumViewDot = 0f;
        public bool CorruptNextResult;
        protected override void OnCreate()
        { candidates = GetEntityQuery(ComponentType.ReadOnly<CombatTargetCandidate>(), ComponentType.ReadWrite<CombatTargetEvaluation>()); }
        protected override void OnStep()
        {
            if (!HasSealedStage<AutoTargetingResolveCommitSystem>())
                throw new InvalidOperationException("Auto targeting requires its Resolve/Commit stage.");
            if (!EntityManager.HasComponent<CombatTargetAssignment>(SessionEntity))
                EntityManager.AddComponentData(SessionEntity, default(CombatTargetAssignment));
            var players = EntityManager.GetBuffer<PlayerStepElement>(SessionEntity);
            PlayerStepElement player = default;
            for (int i = 0; i < players.Length; ++i)
                if (players[i].Valid != 0 && players[i].PlayerId == PlayerId) { player = players[i]; break; }
            if (player.Valid == 0 || !math.all(math.isfinite(player.EndPosition)) || !math.all(math.isfinite(player.ViewForward)))
                throw new InvalidOperationException("Auto targeting has no valid canonical player view.");
            var forward = math.normalizesafe(player.ViewForward);
            if (math.lengthsq(forward) == 0f || !math.isfinite(MaxRange) || MaxRange < 0f
                || !math.isfinite(MinimumViewDot) || MinimumViewDot < -1f || MinimumViewDot > 1f)
                throw new InvalidOperationException("Auto targeting prototype configuration is invalid.");
            var ordinal = CorruptNextResult ? Step.Ordinal + 1 : Step.Ordinal;
            CorruptNextResult = false;
            var entities = candidates.ToEntityArray(Allocator.TempJob);
            Dependency = new EvaluateCandidatesJob
            {
                Entities = entities, Candidates = GetComponentLookup<CombatTargetCandidate>(true),
                Evaluations = GetComponentLookup<CombatTargetEvaluation>(false),
                Origin = player.EndPosition,
                Forward = forward,
                MaxDistanceSq = MaxRange * MaxRange,
                MinimumViewDot = MinimumViewDot,
                Generation = Step.Generation,
                Tick = Step.Tick,
                Ordinal = ordinal
            }.Schedule(Dependency);
            Dependency = entities.Dispose(Dependency);
        }
        private struct EvaluateCandidatesJob : IJob
        {
            [ReadOnly] public NativeArray<Entity> Entities;
            [ReadOnly] public ComponentLookup<CombatTargetCandidate> Candidates;
            public ComponentLookup<CombatTargetEvaluation> Evaluations;
            public float3 Origin, Forward;
            public float MaxDistanceSq, MinimumViewDot;
            public ulong Generation, Ordinal;
            public uint Tick;
            public void Execute()
            {
                for (int i = 0; i < Entities.Length; ++i)
                {
                var candidate = Candidates[Entities[i]];
                var delta = candidate.Anchor - Origin;
                var distanceSq = math.lengthsq(delta);
                var direction = math.normalizesafe(delta);
                var viewDot = math.dot(Forward, direction);
                bool valid = candidate.Targetable != 0 && candidate.StableId != 0
                    && math.all(math.isfinite(candidate.Anchor)) && math.isfinite(distanceSq) && math.isfinite(viewDot)
                    && distanceSq <= MaxDistanceSq && viewDot >= MinimumViewDot;
                Evaluations[Entities[i]] = new CombatTargetEvaluation
                {
                    Generation = Generation, Tick = Tick, Ordinal = Ordinal, StableId = candidate.StableId,
                    Eligible = valid ? (byte)1 : (byte)0, DistanceSq = distanceSq, ViewDot = viewDot
                };
                }
            }
        }
    }

    [DisableAutoCreation]
    [UpdateAfter(typeof(AutoTargetingProducerSystem))]
    [UpdateBefore(typeof(SimulationCompleteSystem))]
    public partial class AutoTargetingResolveCommitSystem : CombatStepSystem
    {
        private EntityQuery candidates;
        protected override void OnCreate()
        {
            candidates = GetEntityQuery(ComponentType.ReadOnly<CombatTargetCandidate>(), ComponentType.ReadOnly<CombatTargetEvaluation>());
        }
        protected override void OnStep()
        {
            EntityManager.CompleteDependencyBeforeRO<CombatTargetEvaluation>();
            var producer = World.GetExistingSystemManaged<AutoTargetingProducerSystem>();
            if (producer == null || !EntityManager.HasComponent<CombatTargetAssignment>(SessionEntity))
                throw new InvalidOperationException("Auto targeting result storage is unavailable.");
            bool found = false; ulong stableId = 0; float dot = 0f, distanceSq = 0f;
            using (var entities = candidates.ToEntityArray(Unity.Collections.Allocator.Temp))
            for (int i = 0; i < entities.Length; ++i)
            {
                var result = EntityManager.GetComponentData<CombatTargetEvaluation>(entities[i]);
                if (result.Generation != Step.Generation || result.Tick != Step.Tick || result.Ordinal != Step.Ordinal)
                    throw new InvalidOperationException("Auto targeting evaluation does not belong to the active step.");
                if (result.Eligible == 0) continue;
                if (result.Eligible != 1 || result.StableId == 0 || !math.isfinite(result.ViewDot) || !math.isfinite(result.DistanceSq))
                    throw new InvalidOperationException("Auto targeting evaluation is invalid.");
                if (!found || result.ViewDot > dot || (result.ViewDot == dot && (result.DistanceSq < distanceSq
                    || (result.DistanceSq == distanceSq && result.StableId < stableId))))
                { found = true; stableId = result.StableId; dot = result.ViewDot; distanceSq = result.DistanceSq; }
            }
            EntityManager.SetComponentData(SessionEntity, new CombatTargetAssignment
            {
                Generation = Step.Generation, Tick = Step.Tick, Ordinal = Step.Ordinal,
                PlayerId = producer.PlayerId, WeaponId = producer.WeaponId,
                HasTarget = found ? (byte)1 : (byte)0, TargetStableId = found ? stableId : 0
            });
        }
    }
}
