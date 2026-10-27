using System;

namespace RumOverboard.Gameplay.Attributes
{
    /// <summary>
    /// Stable numeric id of an <see cref="AttributeDefinition"/> (stat or resource).
    /// Assigned by <c>RumOverboard ▸ Attributes ▸ Rebuild Registry</c> and identical on every peer.
    /// 0 = none. Typed constants live in the generated <c>Attr</c> class.
    /// </summary>
    public readonly struct AttributeId : IEquatable<AttributeId>
    {
        public readonly ushort Value;

        public AttributeId(ushort value) => Value = value;

        public bool IsValid => Value != 0;

        public bool Equals(AttributeId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is AttributeId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => $"Attr#{Value}";
        public static bool operator ==(AttributeId a, AttributeId b) => a.Value == b.Value;
        public static bool operator !=(AttributeId a, AttributeId b) => a.Value != b.Value;
    }

    /// <summary>
    /// Id of a <see cref="FlagDefinition"/>: 1..64, maps to one bit of a ulong mask. 0 = none.
    /// Typed constants live in the generated <c>AttrFlag</c> class.
    /// </summary>
    public readonly struct FlagId : IEquatable<FlagId>
    {
        public const int MaxFlags = 64;

        public readonly byte Value;

        public FlagId(byte value) => Value = value;

        public bool IsValid => Value != 0 && Value <= MaxFlags;
        public ulong Mask => IsValid ? 1UL << (Value - 1) : 0UL;

        public bool Equals(FlagId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is FlagId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => $"Flag#{Value}";
        public static bool operator ==(FlagId a, FlagId b) => a.Value == b.Value;
        public static bool operator !=(FlagId a, FlagId b) => a.Value != b.Value;
    }

    /// <summary>
    /// Id of an <see cref="EffectDefinition"/>. 0 = none.
    /// Typed constants live in the generated <c>AttrEffect</c> class.
    /// </summary>
    public readonly struct EffectId : IEquatable<EffectId>
    {
        public readonly ushort Value;

        public EffectId(ushort value) => Value = value;

        public bool IsValid => Value != 0;

        public bool Equals(EffectId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is EffectId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => $"Effect#{Value}";
        public static bool operator ==(EffectId a, EffectId b) => a.Value == b.Value;
        public static bool operator !=(EffectId a, EffectId b) => a.Value != b.Value;
    }

    /// <summary>
    /// Handle that groups modifiers + flag grants so they can be removed in one call
    /// (an effect application, a cold zone, a held object…). 0 = none.
    /// Host-local: never replicated, so a plain process-wide counter is enough.
    /// </summary>
    public readonly struct ModifierSource : IEquatable<ModifierSource>
    {
        private static int _next;

        public readonly int Value;

        public ModifierSource(int value) => Value = value;

        public bool IsValid => Value != 0;

        public static ModifierSource New() => new ModifierSource(++_next);

        public bool Equals(ModifierSource other) => Value == other.Value;
        public override bool Equals(object obj) => obj is ModifierSource other && Equals(other);
        public override int GetHashCode() => Value;
        public static bool operator ==(ModifierSource a, ModifierSource b) => a.Value == b.Value;
        public static bool operator !=(ModifierSource a, ModifierSource b) => a.Value != b.Value;
    }
}
