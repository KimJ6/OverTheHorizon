using UnityEngine;
using OverTheHorizon.Player.Input;
using OverTheHorizon.Player.View;

namespace OverTheHorizon.LocalSimulation
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class PlayerControlSnapshotPublisher : MonoBehaviour
    {
        private PlayerInputReader reader;
        private PlayerViewState view;
        private ulong generation;
        private uint playerId;
        private bool publishing;
        public PlayerControlSnapshot Latest { get; private set; }
        public bool BindingMatches(PlayerInputReader expectedReader, PlayerViewState expectedView, ulong expectedGeneration, uint expectedId)
            => reader == expectedReader && view == expectedView && generation == expectedGeneration && playerId == expectedId;
        public void Bind(PlayerInputReader reader, PlayerViewState view, ulong generation, uint playerId)
        { this.reader = reader; this.view = view; this.generation = generation; this.playerId = playerId; Latest = default; }
        public void SetPublishing(bool enabled)
        { if (!enabled || !publishing) Latest = default; publishing = enabled; }
        private void Update()
        {
            if (!publishing || reader == null || view == null) return;
            var raw = reader.RawSnapshot;
            Latest = view.InputReader == reader && raw.Valid && raw.Sequence == view.LastAppliedRawSequence
                ? new PlayerControlSnapshot(generation, playerId, raw, view) : default;
        }
        private void OnDisable() { Latest = default; }
        public void Unbind() { publishing = false; reader = null; view = null; Latest = default; }
    }
}
