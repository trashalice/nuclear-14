using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Special;

[Serializable, NetSerializable]
public enum SpecialStat : byte
{
    // Keep this enum compact and stable: profiles, network messages, and saved
    // data all refer to these values.
    Strength,
    Perception,
    Endurance,
    Charisma,
    Intelligence,
    Agility,
    Luck,
}

public static class SpecialStats
{
    // Single canonical iteration order for display, serialization helpers, and commands.
    public static readonly SpecialStat[] All =
    {
        SpecialStat.Strength,
        SpecialStat.Perception,
        SpecialStat.Endurance,
        SpecialStat.Charisma,
        SpecialStat.Intelligence,
        SpecialStat.Agility,
        SpecialStat.Luck,
    };

    public static bool IsEnabled(SpecialStat stat)
    {
        // Centralizes active-stat checks so old/unknown enum values degrade safely.
        return stat is SpecialStat.Strength
            or SpecialStat.Perception
            or SpecialStat.Endurance
            or SpecialStat.Charisma
            or SpecialStat.Intelligence
            or SpecialStat.Agility
            or SpecialStat.Luck;
    }
}
