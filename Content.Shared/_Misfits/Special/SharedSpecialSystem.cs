using Content.Shared._Misfits.Special.Components;
using Content.Shared._Misfits.Special.Prototypes;
using Content.Shared.Chemistry;
using Content.Shared.Movement.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._Misfits.Special;

public sealed class SharedSpecialSystem : EntitySystem
{
    private const string TuningPrototypeId = "MisfitsSpecialTuning";
    private const int IntelligenceSolutionScanThreshold = 8;

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;

    private readonly List<TemporaryModifierEntry> _temporaryModifiers = new();

    public override void Initialize()
    {
        base.Initialize();

        UpdatesOutsidePrediction = true;

        SubscribeLocalEvent<SpecialComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<SpecialComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<SpecialComponent, SolutionScanEvent>(OnSolutionScan);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        for (var i = _temporaryModifiers.Count - 1; i >= 0; i--)
        {
            var entry = _temporaryModifiers[i];
            if (!entry.Expires || entry.ExpiresAt > now)
                continue;

            _temporaryModifiers.RemoveAt(i);
            if (TryComp<SpecialComponent>(entry.Entity, out var comp))
                ApplyTemporary(entry.Entity, comp, entry.Stat, -entry.Value);
        }
    }

    private void OnStartup(Entity<SpecialComponent> ent, ref ComponentStartup args)
    {
        Normalize(ent.Owner, ent.Comp);
    }

    private void OnShutdown(Entity<SpecialComponent> ent, ref ComponentShutdown args)
    {
        _temporaryModifiers.RemoveAll(entry => entry.Entity == ent.Owner);

        var ev = new SpecialShutdownEvent(ent.Owner);
        RaiseLocalEvent(ent.Owner, ref ev, true);
    }

    private void OnSolutionScan(Entity<SpecialComponent> ent, ref SolutionScanEvent args)
    {
        if (GetEffective(ent.Owner, SpecialStat.Intelligence, ent.Comp) >= IntelligenceSolutionScanThreshold)
            args.CanScan = true;
    }

    public SpecialTuningPrototype GetTuning()
    {
        return _prototype.TryIndex<SpecialTuningPrototype>(TuningPrototypeId, out var tuning)
            ? tuning
            : SpecialTuningPrototype.Fallback;
    }

    public int GetBase(EntityUid uid, SpecialStat stat, SpecialComponent? component = null)
    {
        if (!SpecialStats.IsEnabled(stat))
            return SpecialProfile.DefaultValue;

        if (!Resolve(uid, ref component, false))
            return SpecialProfile.DefaultValue;

        return GetBase(component, stat);
    }

    public int GetModifier(EntityUid uid, SpecialStat stat, SpecialComponent? component = null)
    {
        if (!SpecialStats.IsEnabled(stat))
            return 0;

        if (!Resolve(uid, ref component, false))
            return 0;

        return GetModifier(component, stat);
    }

    public int GetUnclampedEffective(EntityUid uid, SpecialStat stat, SpecialComponent? component = null)
    {
        if (!SpecialStats.IsEnabled(stat))
            return SpecialProfile.DefaultValue;

        if (!Resolve(uid, ref component, false))
            return SpecialProfile.DefaultValue;

        return GetBase(component, stat) + GetModifier(component, stat);
    }

    public int GetEffective(EntityUid uid, SpecialStat stat, SpecialComponent? component = null)
    {
        return Math.Clamp(GetUnclampedEffective(uid, stat, component), SpecialProfile.Minimum, SpecialProfile.Maximum);
    }

    public int GetEffectDelta(EntityUid uid, SpecialStat stat, SpecialComponent? component = null)
    {
        return GetEffective(uid, stat, component) - SpecialProfile.DefaultValue;
    }

    public float GetCurvedEffectDelta(EntityUid uid, SpecialStat stat, SpecialComponent? component = null)
    {
        return GetCurvedEffectDelta(GetEffective(uid, stat, component));
    }

    public static float GetCurvedEffectDelta(int effective)
    {
        return Math.Clamp(effective, SpecialProfile.Minimum, SpecialProfile.Maximum) switch
        {
            1 => -5f,
            2 => -3.5f,
            3 => -2.25f,
            4 => -1f,
            5 => 0f,
            6 => 1f,
            7 => 2.25f,
            8 => 3.75f,
            9 => 5.5f,
            _ => 7.5f,
        };
    }

    public float GetCurvedEffectScale(
        EntityUid uid,
        SpecialStat stat,
        float valueAtOne,
        float valueAtTen,
        SpecialComponent? component = null)
    {
        return GetCurvedEffectScale(GetCurvedEffectDelta(uid, stat, component), valueAtOne, valueAtTen);
    }

    public static float GetCurvedEffectScale(float curvedDelta, float valueAtOne, float valueAtTen)
    {
        if (curvedDelta > 0f)
            return valueAtTen * curvedDelta / GetCurvedEffectDelta(SpecialProfile.Maximum);

        if (curvedDelta < 0f)
            return valueAtOne * curvedDelta / GetCurvedEffectDelta(SpecialProfile.Minimum);

        return 0f;
    }

    public static int GetCharismaLoadoutPointModifier(int charisma)
    {
        return (int) Math.Round(GetCurvedEffectDelta(charisma) * 2f, MidpointRounding.AwayFromZero);
    }

    public bool HasRequirement(EntityUid uid, SpecialStat stat, int minimum, SpecialComponent? component = null)
    {
        return GetEffective(uid, stat, component) >= minimum;
    }

    public bool TrySetBase(EntityUid uid, SpecialStat stat, int value, SpecialComponent? component = null)
    {
        if (!SpecialStats.IsEnabled(stat) || !SpecialProfile.IsWithinBounds(value) || !Resolve(uid, ref component, false))
            return false;

        SetBase(component, stat, value);
        Dirty(uid, component);
        RaiseSpecialChanged(uid);
        return true;
    }

    public bool TrySetBaseValues(EntityUid uid, SpecialProfile profile, SpecialComponent? component = null)
    {
        profile = SpecialProfile.EnsureValid(profile);

        if (!Resolve(uid, ref component, false))
            return false;

        component.BaseStrength = profile.Strength;
        component.BasePerception = profile.Perception;
        component.BaseEndurance = profile.Endurance;
        component.BaseCharisma = profile.Charisma;
        component.BaseIntelligence = profile.Intelligence;
        component.BaseAgility = profile.Agility;
        component.BaseLuck = profile.Luck;

        Normalize(uid, component);
        Dirty(uid, component);
        RaiseSpecialChanged(uid);
        return true;
    }

    public bool TryModifyTemporary(
        EntityUid uid,
        SpecialStat stat,
        int modifier,
        TimeSpan? duration = null,
        string source = "",
        SpecialComponent? component = null)
    {
        if (modifier == 0 || !SpecialStats.IsEnabled(stat) || !Resolve(uid, ref component, false))
            return false;

        ApplyTemporary(uid, component, stat, modifier);

        _temporaryModifiers.Add(new TemporaryModifierEntry(
            uid,
            stat,
            modifier,
            source,
            duration.HasValue,
            duration.HasValue ? _timing.CurTime + duration.Value : TimeSpan.Zero));

        return true;
    }

    public void ClearTemporaryModifiers(EntityUid uid, string? source = null, SpecialComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return;

        for (var i = _temporaryModifiers.Count - 1; i >= 0; i--)
        {
            var entry = _temporaryModifiers[i];
            if (entry.Entity != uid || source != null && entry.Source != source)
                continue;

            _temporaryModifiers.RemoveAt(i);
            ApplyTemporary(uid, component, entry.Stat, -entry.Value);
        }
    }

    public float GetLuckRollChance(EntityUid uid, float baseChance, float chancePerPoint, SpecialComponent? component = null)
    {
        var delta = GetCurvedEffectDelta(uid, SpecialStat.Luck, component);
        return Math.Clamp(baseChance + delta * chancePerPoint, 0f, 1f);
    }

    public SpecialProfile ToProfile(EntityUid uid, SpecialComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return SpecialProfile.Default();

        return new SpecialProfile
        {
            Strength = GetBase(component, SpecialStat.Strength),
            Perception = GetBase(component, SpecialStat.Perception),
            Endurance = GetBase(component, SpecialStat.Endurance),
            Charisma = GetBase(component, SpecialStat.Charisma),
            Intelligence = GetBase(component, SpecialStat.Intelligence),
            Agility = GetBase(component, SpecialStat.Agility),
            Luck = GetBase(component, SpecialStat.Luck),
        };
    }

    private void Normalize(EntityUid uid, SpecialComponent component)
    {
        component.BaseStrength = Math.Clamp(component.BaseStrength, SpecialProfile.Minimum, SpecialProfile.Maximum);
        component.BasePerception = Math.Clamp(component.BasePerception, SpecialProfile.Minimum, SpecialProfile.Maximum);
        component.BaseEndurance = Math.Clamp(component.BaseEndurance, SpecialProfile.Minimum, SpecialProfile.Maximum);
        component.BaseCharisma = Math.Clamp(component.BaseCharisma, SpecialProfile.Minimum, SpecialProfile.Maximum);
        component.BaseIntelligence = Math.Clamp(component.BaseIntelligence, SpecialProfile.Minimum, SpecialProfile.Maximum);
        component.BaseAgility = Math.Clamp(component.BaseAgility, SpecialProfile.Minimum, SpecialProfile.Maximum);
        component.BaseLuck = Math.Clamp(component.BaseLuck, SpecialProfile.Minimum, SpecialProfile.Maximum);
        Dirty(uid, component);
    }

    private void ApplyTemporary(EntityUid uid, SpecialComponent component, SpecialStat stat, int modifier)
    {
        SetModifier(component, stat, GetModifier(component, stat) + modifier);
        Dirty(uid, component);
        RaiseSpecialChanged(uid);
    }

    private void RaiseSpecialChanged(EntityUid uid)
    {
        var ev = new SpecialChangedEvent(uid);
        RaiseLocalEvent(uid, ref ev, true);
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    private static int GetBase(SpecialComponent component, SpecialStat stat)
    {
        return stat switch
        {
            SpecialStat.Strength => component.BaseStrength,
            SpecialStat.Perception => component.BasePerception,
            SpecialStat.Endurance => component.BaseEndurance,
            SpecialStat.Charisma => component.BaseCharisma,
            SpecialStat.Intelligence => component.BaseIntelligence,
            SpecialStat.Agility => component.BaseAgility,
            SpecialStat.Luck => component.BaseLuck,
            _ => SpecialProfile.DefaultValue,
        };
    }

    private static int GetModifier(SpecialComponent component, SpecialStat stat)
    {
        return stat switch
        {
            SpecialStat.Strength => component.TemporaryStrengthModifier,
            SpecialStat.Perception => component.TemporaryPerceptionModifier,
            SpecialStat.Endurance => component.TemporaryEnduranceModifier,
            SpecialStat.Charisma => component.TemporaryCharismaModifier,
            SpecialStat.Intelligence => component.TemporaryIntelligenceModifier,
            SpecialStat.Agility => component.TemporaryAgilityModifier,
            SpecialStat.Luck => component.TemporaryLuckModifier,
            _ => 0,
        };
    }

    private static void SetBase(SpecialComponent component, SpecialStat stat, int value)
    {
        switch (stat)
        {
            case SpecialStat.Strength:
                component.BaseStrength = value;
                break;
            case SpecialStat.Perception:
                component.BasePerception = value;
                break;
            case SpecialStat.Endurance:
                component.BaseEndurance = value;
                break;
            case SpecialStat.Charisma:
                component.BaseCharisma = value;
                break;
            case SpecialStat.Intelligence:
                component.BaseIntelligence = value;
                break;
            case SpecialStat.Agility:
                component.BaseAgility = value;
                break;
            case SpecialStat.Luck:
                component.BaseLuck = value;
                break;
        }
    }

    private static void SetModifier(SpecialComponent component, SpecialStat stat, int value)
    {
        switch (stat)
        {
            case SpecialStat.Strength:
                component.TemporaryStrengthModifier = value;
                break;
            case SpecialStat.Perception:
                component.TemporaryPerceptionModifier = value;
                break;
            case SpecialStat.Endurance:
                component.TemporaryEnduranceModifier = value;
                break;
            case SpecialStat.Charisma:
                component.TemporaryCharismaModifier = value;
                break;
            case SpecialStat.Intelligence:
                component.TemporaryIntelligenceModifier = value;
                break;
            case SpecialStat.Agility:
                component.TemporaryAgilityModifier = value;
                break;
            case SpecialStat.Luck:
                component.TemporaryLuckModifier = value;
                break;
        }
    }

    private readonly record struct TemporaryModifierEntry(
        EntityUid Entity,
        SpecialStat Stat,
        int Value,
        string Source,
        bool Expires,
        TimeSpan ExpiresAt);
}
