using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Special;

/// <summary>
/// Serializable character-profile copy of base SPECIAL values.
/// Runtime modifiers live on <see cref="SpecialComponent"/>.
/// </summary>
[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class SpecialProfile
{
    // SPECIAL uses Fallout-style 1-10 stats with 5 as the neutral midpoint.
    public const int Minimum = 1;
    public const int Maximum = 10;
    public const int DefaultValue = 5;

    // Players start at the default total and may distribute a small bonus pool.
    public const int BonusPoints = 5;
    public const int ActiveStatCount = 7;
    public const int DefaultTotal = DefaultValue * ActiveStatCount;
    public const int MaxTotal = DefaultTotal + BonusPoints;

    [DataField]
    public int Strength = DefaultValue;

    [DataField]
    public int Perception = DefaultValue;

    [DataField]
    public int Endurance = DefaultValue;

    [DataField]
    public int Charisma = DefaultValue;

    [DataField]
    public int Intelligence = DefaultValue;

    [DataField]
    public int Agility = DefaultValue;

    [DataField]
    public int Luck = DefaultValue;

    public SpecialProfile()
    {
    }

    public SpecialProfile(SpecialProfile other)
    {
        Strength = other.Strength;
        Perception = other.Perception;
        Endurance = other.Endurance;
        Charisma = other.Charisma;
        Intelligence = other.Intelligence;
        Agility = other.Agility;
        Luck = other.Luck;
    }

    public static SpecialProfile Default() => new();

    public SpecialProfile Clone() => new(this);

    public int Total => Strength + Perception + Endurance + Charisma + Intelligence + Agility + Luck;

    public int AvailablePoints => MaxTotal - Total;

    public bool IsValid =>
        IsWithinBounds(Strength) &&
        IsWithinBounds(Perception) &&
        IsWithinBounds(Endurance) &&
        IsWithinBounds(Charisma) &&
        IsWithinBounds(Intelligence) &&
        IsWithinBounds(Agility) &&
        IsWithinBounds(Luck) &&
        Total <= MaxTotal;

    public static bool IsWithinBounds(int value) => value is >= Minimum and <= Maximum;

    public static SpecialProfile EnsureValid(SpecialProfile? profile)
    {
        // Invalid profiles fall back completely instead of being partially
        // clamped, so malformed or over-budget input cannot preserve advantages.
        if (profile == null)
            return Default();

        var clone = profile.Clone();

        return clone.IsValid ? clone : Default();
    }

    public int Get(SpecialStat stat)
    {
        return stat switch
        {
            SpecialStat.Strength => Strength,
            SpecialStat.Perception => Perception,
            SpecialStat.Endurance => Endurance,
            SpecialStat.Charisma => Charisma,
            SpecialStat.Intelligence => Intelligence,
            SpecialStat.Agility => Agility,
            SpecialStat.Luck => Luck,
            _ => DefaultValue,
        };
    }

    public void Set(SpecialStat stat, int value)
    {
        // Setters clamp individual values, but full-profile budget validation
        // remains the caller's responsibility through IsValid/EnsureValid.
        value = Math.Clamp(value, Minimum, Maximum);

        switch (stat)
        {
            case SpecialStat.Strength:
                Strength = value;
                break;
            case SpecialStat.Perception:
                Perception = value;
                break;
            case SpecialStat.Endurance:
                Endurance = value;
                break;
            case SpecialStat.Charisma:
                Charisma = value;
                break;
            case SpecialStat.Intelligence:
                Intelligence = value;
                break;
            case SpecialStat.Agility:
                Agility = value;
                break;
            case SpecialStat.Luck:
                Luck = value;
                break;
        }
    }

    public SpecialProfile With(SpecialStat stat, int value)
    {
        var clone = Clone();
        clone.Set(stat, value);
        return clone;
    }

    public bool MemberwiseEquals(SpecialProfile other) =>
        Strength == other.Strength &&
        Perception == other.Perception &&
        Endurance == other.Endurance &&
        Charisma == other.Charisma &&
        Intelligence == other.Intelligence &&
        Agility == other.Agility &&
        Luck == other.Luck;

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || obj is SpecialProfile other && MemberwiseEquals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Strength, Perception, Endurance, Charisma, Intelligence, Agility, Luck);
    }

    public string ToCompactString() =>
        $"S{Strength} P{Perception} E{Endurance} C{Charisma} I{Intelligence} A{Agility} L{Luck}";
}
