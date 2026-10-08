using System;
using System.Globalization;
using System.IO;
using Unity.Collections;
using OverTheHorizon.Simulation;
using OverTheHorizon.Timing;

namespace OverTheHorizon.LocalSimulation
{
    public struct SimulationTraceRecord
    {
        public SimulationStepContext Context;
        public bool Completed;
        public PlayerStepElement P0, P1, P2, P3;
    }
    public sealed class SimulationTraceBuffer
    {
        private readonly SimulationTraceRecord[] records;
        private int next;
        public int Count { get; private set; }
        public SimulationTraceBuffer(int capacity)
        { if (capacity < 1 || capacity > 65536) throw new ArgumentOutOfRangeException(nameof(capacity)); records = new SimulationTraceRecord[capacity]; }
        public void Append(SimulationStepContext context, bool completed, NativeArray<PlayerStepElement> players)
        {
            if (!players.IsCreated || players.Length != 4) throw new ArgumentException("Trace needs four canonical slots.");
            records[next] = new SimulationTraceRecord { Context = context, Completed = completed,
                P0 = players[0], P1 = players[1], P2 = players[2], P3 = players[3] };
            next = (next + 1) % records.Length; Count = Math.Min(Count + 1, records.Length);
        }
        public SimulationTraceRecord Read(int index)
        { if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index)); return records[(next - Count + index + records.Length) % records.Length]; }
        public void ExportCsv(string path)
        {
            using (var writer = new StreamWriter(path))
            {
                writer.WriteLine("generation,tick,ordinal,completed,slot,player,input_sequence,start_tick,end_tick,start_x,end_x");
                for (int i = 0; i < Count; ++i)
                {
                    var record = Read(i);
                    for (int slot = 0; slot < 4; ++slot)
                    {
                        var p = slot == 0 ? record.P0 : slot == 1 ? record.P1 : slot == 2 ? record.P2 : record.P3;
                        if (p.Valid == 0) continue;
                        writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9:R},{10:R}",
                            record.Context.Generation, record.Context.Tick, record.Context.Ordinal, record.Completed, slot,
                            p.PlayerId, p.InputSequence, p.StartStateTick, p.EndStateTick, p.StartPosition.x, p.EndPosition.x));
                    }
                }
            }
        }
    }
}
