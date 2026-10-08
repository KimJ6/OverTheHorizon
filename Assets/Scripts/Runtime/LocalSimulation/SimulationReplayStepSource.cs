using System;
using System.IO;
using Unity.Mathematics;
using OverTheHorizon.Simulation;
using OverTheHorizon.Timing;

namespace OverTheHorizon.LocalSimulation
{
    /// <summary>Versioned canonical frames for ECS replay. No device input, camera sampling or PhysX re-simulation.</summary>
    public sealed class SimulationReplayStepSource
    {
        private const uint Magic = 0x48545A31;
        private const int Schema = 1;
        private readonly SimulationTraceRecord[] records;
        private int index;
        public int Count => records.Length;
        public SimulationReplayStepSource(string path)
        {
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Schema) throw new InvalidDataException("Unsupported timing trace.");
                int count = reader.ReadInt32();
                if (count < 0 || count > 65536) throw new InvalidDataException("Trace capacity exceeded.");
                records = new SimulationTraceRecord[count];
                for (int i = 0; i < count; ++i)
                {
                    ulong generation = reader.ReadUInt64(); uint tick = reader.ReadUInt32(); ulong ordinal = reader.ReadUInt64();
                    var config = new SimulationTimingConfig(reader.ReadInt32(), reader.ReadInt32());
                    var record = new SimulationTraceRecord { Context = new SimulationStepContext(generation, tick, ordinal, config),
                        Completed = reader.ReadBoolean(), P0 = ReadPlayer(reader), P1 = ReadPlayer(reader),
                        P2 = ReadPlayer(reader), P3 = ReadPlayer(reader) };
                    records[i] = record;
                }
                if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Unexpected trailing trace data.");
            }
        }
        public bool TryRead(out SimulationTraceRecord record)
        { if (index == records.Length) { record = default; return false; } record = records[index++]; return true; }
        public void Reset() { index = 0; }
        public static void Export(SimulationTraceBuffer trace, string path)
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(Magic); writer.Write(Schema); writer.Write(trace.Count);
                for (int i = 0; i < trace.Count; ++i)
                {
                    var r = trace.Read(i); var c = r.Context;
                    writer.Write(c.Generation); writer.Write(c.Tick); writer.Write(c.Ordinal);
                    writer.Write(c.TickRate); writer.Write(c.ConfigVersion); writer.Write(r.Completed);
                    WritePlayer(writer, r.P0); WritePlayer(writer, r.P1); WritePlayer(writer, r.P2); WritePlayer(writer, r.P3);
                }
            }
        }
        private static void WriteVector(BinaryWriter w, float3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        private static float3 ReadVector(BinaryReader r) => new float3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        private static void WriteRotation(BinaryWriter w, quaternion q)
        { w.Write(q.value.x); w.Write(q.value.y); w.Write(q.value.z); w.Write(q.value.w); }
        private static quaternion ReadRotation(BinaryReader r) => new quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        private static void WritePlayer(BinaryWriter w, PlayerStepElement p)
        {
            w.Write(p.Valid); w.Write(p.PlayerId); w.Write(p.SlotGeneration); w.Write(p.StartStateTick); w.Write(p.EndStateTick);
            w.Write(p.Generation); w.Write(p.ConsumedOrdinal); w.Write(p.InputSequence); w.Write(p.EdgeHighWatermark);
            WriteVector(w, p.StartPosition); WriteVector(w, p.EndPosition); WriteVector(w, p.StartVelocity);
            WriteVector(w, p.EndVelocity); WriteVector(w, p.EndAngularVelocity);
            WriteRotation(w, p.StartRotation); WriteRotation(w, p.EndRotation); WriteRotation(w, p.SteeringRotation);
            WriteVector(w, p.SteeringForward); WriteVector(w, p.SteeringRight); WriteVector(w, p.ViewForward);
            WriteVector(w, p.Axes); WriteVector(w, p.Gravity); w.Write(p.LogicalYaw); w.Write(p.LogicalPitch);
            w.Write(p.FreeLook); w.Write(p.Maneuver);
        }
        private static PlayerStepElement ReadPlayer(BinaryReader r)
        {
            return new PlayerStepElement { Valid = r.ReadByte(), PlayerId = r.ReadUInt32(), SlotGeneration = r.ReadUInt32(),
                StartStateTick = r.ReadUInt32(), EndStateTick = r.ReadUInt32(), Generation = r.ReadUInt64(),
                ConsumedOrdinal = r.ReadUInt64(), InputSequence = r.ReadUInt64(), EdgeHighWatermark = r.ReadUInt64(),
                StartPosition = ReadVector(r), EndPosition = ReadVector(r), StartVelocity = ReadVector(r), EndVelocity = ReadVector(r),
                EndAngularVelocity = ReadVector(r), StartRotation = ReadRotation(r), EndRotation = ReadRotation(r), SteeringRotation = ReadRotation(r),
                SteeringForward = ReadVector(r), SteeringRight = ReadVector(r), ViewForward = ReadVector(r), Axes = ReadVector(r), Gravity = ReadVector(r),
                LogicalYaw = r.ReadSingle(), LogicalPitch = r.ReadSingle(), FreeLook = r.ReadByte(), Maneuver = r.ReadByte() };
        }
    }
}
