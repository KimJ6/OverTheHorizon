using System;

namespace OverTheHorizon.Player.Input
{
    public readonly struct InputEdge
    {
        public readonly ulong Sequence;
        public readonly uint Kind;
        public InputEdge(ulong sequence, uint kind) { Sequence = sequence; Kind = kind; }
    }
    /// <summary>Bounded, main-thread, non-destructive reads. ACK only after successful authority completion.</summary>
    public sealed class InputEdgeBuffer
    {
        private readonly InputEdge[] items;
        private int start, count;
        private ulong sequence;
        public int Count => count;
        public ulong HighWatermark => count == 0 ? 0 : items[(start + count - 1) % items.Length].Sequence;
        public InputEdgeBuffer(int capacity = 32)
        { if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity)); items = new InputEdge[capacity]; }
        public void Enqueue(uint kind)
        {
            if (count == items.Length) throw new InvalidOperationException("Input edge capacity exceeded.");
            items[(start + count) % items.Length] = new InputEdge(checked(++sequence), kind); ++count;
        }
        public InputEdge Read(int index)
        { if ((uint)index >= (uint)count) throw new ArgumentOutOfRangeException(nameof(index)); return items[(start + index) % items.Length]; }
        public void AckThrough(ulong highWatermark)
        {
            while (count > 0 && items[start].Sequence <= highWatermark)
            { items[start] = default; start = (start + 1) % items.Length; --count; }
        }
        public void Clear() { Array.Clear(items, 0, items.Length); start = count = 0; }
    }
}
