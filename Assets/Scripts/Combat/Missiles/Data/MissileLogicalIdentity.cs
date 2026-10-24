using System;

namespace OverTheHorizon.Combat.Missiles
{
    /// <summary>
    /// Salvo identity is logical gameplay data. It is not an Entity index, pool slot,
    /// or a registration in the later Combat Target Registry.
    /// </summary>
    public readonly struct SalvoLogicalKey : IEquatable<SalvoLogicalKey>
    {
        public readonly ulong EmitterStableId;
        public readonly uint SalvoSequence;

        public SalvoLogicalKey(ulong emitterStableId, uint salvoSequence)
        {
            EmitterStableId = emitterStableId;
            SalvoSequence = salvoSequence;
        }

        public bool IsValid => EmitterStableId != 0;
        public bool Equals(SalvoLogicalKey other)
            => EmitterStableId == other.EmitterStableId && SalvoSequence == other.SalvoSequence;
        public override bool Equals(object obj) => obj is SalvoLogicalKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(EmitterStableId, SalvoSequence);
        public static bool operator ==(SalvoLogicalKey left, SalvoLogicalKey right) => left.Equals(right);
        public static bool operator !=(SalvoLogicalKey left, SalvoLogicalKey right) => !left.Equals(right);
    }

    /// <summary>Member ordinal remains stable when later spawn or cleanup leaves holes.</summary>
    public readonly struct MemberLogicalKey : IEquatable<MemberLogicalKey>
    {
        public readonly SalvoLogicalKey Salvo;
        public readonly uint MemberOrdinal;

        public MemberLogicalKey(SalvoLogicalKey salvo, uint memberOrdinal)
        {
            Salvo = salvo;
            MemberOrdinal = memberOrdinal;
        }

        public bool IsValid => Salvo.IsValid;
        public bool Equals(MemberLogicalKey other) => Salvo.Equals(other.Salvo) && MemberOrdinal == other.MemberOrdinal;
        public override bool Equals(object obj) => obj is MemberLogicalKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Salvo, MemberOrdinal);
        public static bool operator ==(MemberLogicalKey left, MemberLogicalKey right) => left.Equals(right);
        public static bool operator !=(MemberLogicalKey left, MemberLogicalKey right) => !left.Equals(right);
    }

    /// <summary>
    /// Stable launch-cell identity.  It identifies the emitter-owned physical launch
    /// point without making an Entity index part of a cross-tick command contract.
    /// </summary>
    public readonly struct MissileLaunchCellKey : IEquatable<MissileLaunchCellKey>
    {
        public readonly ulong EmitterStableId;
        public readonly uint CellOrdinal;

        public MissileLaunchCellKey(ulong emitterStableId, uint cellOrdinal)
        {
            EmitterStableId = emitterStableId;
            CellOrdinal = cellOrdinal;
        }

        public bool IsValid => EmitterStableId != 0;
        public bool Equals(MissileLaunchCellKey other)
            => EmitterStableId == other.EmitterStableId && CellOrdinal == other.CellOrdinal;
        public override bool Equals(object obj) => obj is MissileLaunchCellKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(EmitterStableId, CellOrdinal);
        public static bool operator ==(MissileLaunchCellKey left, MissileLaunchCellKey right) => left.Equals(right);
        public static bool operator !=(MissileLaunchCellKey left, MissileLaunchCellKey right) => !left.Equals(right);
    }

    /// <summary>
    /// Player identity for M1/M2 input validation. It deliberately does not invent a
    /// global player stable ID before the authoritative registry owns one.
    /// </summary>
    public readonly struct SessionPlayerKey : IEquatable<SessionPlayerKey>
    {
        public readonly ulong SessionGeneration;
        public readonly uint PlayerId;
        public readonly uint SlotGeneration;

        public SessionPlayerKey(ulong sessionGeneration, uint playerId, uint slotGeneration)
        {
            SessionGeneration = sessionGeneration;
            PlayerId = playerId;
            SlotGeneration = slotGeneration;
        }

        public bool IsValid => SessionGeneration != 0 && PlayerId != 0 && SlotGeneration != 0;
        public bool Equals(SessionPlayerKey other)
            => SessionGeneration == other.SessionGeneration && PlayerId == other.PlayerId && SlotGeneration == other.SlotGeneration;
        public override bool Equals(object obj) => obj is SessionPlayerKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(SessionGeneration, PlayerId, SlotGeneration);
        public static bool operator ==(SessionPlayerKey left, SessionPlayerKey right) => left.Equals(right);
        public static bool operator !=(SessionPlayerKey left, SessionPlayerKey right) => !left.Equals(right);
    }

    /// <summary>
    /// Independent seed domains prevent a future variation draw from changing scheduling
    /// or presentation results. M0 declares domains only; it owns no session seed or RNG.
    /// </summary>
    public enum MissileSeedDomain : uint
    {
        MemberVariation = 0x4D564152,
        Scheduling = 0x4D534348,
        Presentation = 0x4D505245
    }

    /// <summary>
    /// Stateless deterministic derivation for future Salvo and Member variation.
    /// A derived value is a random seed, never a globally unique gameplay identity.
    /// </summary>
    public static class MissileSeedDerivation
    {
        private const ulong SalvoDomain = 0x53414C564F534545ul;

        public static ulong DeriveSalvoSeed(ulong sessionSeed, SalvoLogicalKey salvo)
        {
            if (!salvo.IsValid) throw new ArgumentException("A valid SalvoLogicalKey is required.", nameof(salvo));
            return Mix(sessionSeed ^ SalvoDomain ^ Mix(salvo.EmitterStableId) ^ RotateLeft(salvo.SalvoSequence, 17));
        }

        public static ulong DeriveMemberSeed(ulong salvoSeed, MemberLogicalKey member, MissileSeedDomain purpose)
        {
            if (!member.IsValid) throw new ArgumentException("A valid MemberLogicalKey is required.", nameof(member));
            return Mix(salvoSeed ^ Mix(member.Salvo.EmitterStableId) ^ RotateLeft(member.Salvo.SalvoSequence, 11)
                ^ RotateLeft(member.MemberOrdinal, 29) ^ (ulong)purpose);
        }

        private static ulong RotateLeft(uint value, int offset)
            => ((ulong)value << offset) | ((ulong)value >> (64 - offset));

        private static ulong Mix(ulong value)
        {
            value += 0x9E3779B97F4A7C15ul;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9ul;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBul;
            return value ^ (value >> 31);
        }
    }
}
