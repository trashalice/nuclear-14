using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.Special.Prototypes;

[Prototype("specialTuning")]
public sealed partial class SpecialTuningPrototype : IPrototype
{
    // Code defaults keep the system usable in tests or when the tuning prototype
    // is not loaded. Production values should live in YAML.
    public static readonly SpecialTuningPrototype Fallback = new()
    {
        ID = "Fallback",
    };

    [IdDataField]
    public string ID { get; private set; } = default!;

    // Strength: melee output and heavy/carry handling.
    [DataField("strengthMeleeDamageMultiplierPerPoint")]
    public float StrengthMeleeDamageMultiplierPerPoint = 0.02f;

    [DataField("strengthCarryPullSpeedPenaltyAtOne")]
    public float StrengthCarryPullSpeedPenaltyAtOne = 0.08f;

    [DataField("strengthCarryPullSpeedBonusAtTen")]
    public float StrengthCarryPullSpeedBonusAtTen = 0.10f;

    [DataField("strengthHeavyGunPenaltyAtOne")]
    public float StrengthHeavyGunPenaltyAtOne = 0.18f;

    [DataField("strengthHeavyGunReductionAtTen")]
    public float StrengthHeavyGunReductionAtTen = 0.10f;

    // Perception: ranged accuracy, mining speed, and fire delay.
    [DataField("perceptionSpreadReductionPerPoint")]
    public float PerceptionSpreadReductionPerPoint = 0.004f;

    [DataField("perceptionSpreadPenaltyAtOne")]
    public float PerceptionSpreadPenaltyAtOne = 0.15f;

    [DataField("perceptionSpreadReductionAtTen")]
    public float PerceptionSpreadReductionAtTen = 0.25f;

    [DataField("perceptionMineDelayPenaltyAtOne")]
    public float PerceptionMineDelayPenaltyAtOne = 0.25f;

    [DataField("perceptionMineDelayReductionAtTen")]
    public float PerceptionMineDelayReductionAtTen = 0.25f;

    [DataField("perceptionFireDelayPenaltyAtOne")]
    public float PerceptionFireDelayPenaltyAtOne = 0.08f;

    [DataField("perceptionFireDelayReductionAtTen")]
    public float PerceptionFireDelayReductionAtTen = 0.10f;

    // Endurance: survivability, needs, stamina, and toxin resistance.
    [DataField("enduranceStaminaCritThresholdPerPoint")]
    public float EnduranceStaminaCritThresholdPerPoint = 4f;

    [DataField("enduranceHealthPenaltyAtOne")]
    public float EnduranceHealthPenaltyAtOne = 20f;

    [DataField("enduranceHealthBonusAtTen")]
    public float EnduranceHealthBonusAtTen = 20f;

    [DataField("enduranceNeedDecayPenaltyAtOne")]
    public float EnduranceNeedDecayPenaltyAtOne = 0.15f;

    [DataField("enduranceNeedDecayReductionAtTen")]
    public float EnduranceNeedDecayReductionAtTen = 0.12f;

    [DataField("enduranceStaminaRecoveryPenaltyAtOne")]
    public float EnduranceStaminaRecoveryPenaltyAtOne = 0.15f;

    [DataField("enduranceStaminaRecoveryBonusAtTen")]
    public float EnduranceStaminaRecoveryBonusAtTen = 0.20f;

    [DataField("enduranceToxinDamagePenaltyAtOne")]
    public float EnduranceToxinDamagePenaltyAtOne = 0.12f;

    [DataField("enduranceToxinDamageReductionAtTen")]
    public float EnduranceToxinDamageReductionAtTen = 0.15f;

    // Charisma: economy, loadout points, and presentation hooks.
    [DataField("charismaTradePenaltyAtOne")]
    public float CharismaTradePenaltyAtOne = 0.10f;

    [DataField("charismaTradeBonusAtTen")]
    public float CharismaTradeBonusAtTen = 0.10f;

    // Intelligence: crafting/medical quality-of-life gates.
    [DataField("intelligenceLatheMinimumTimeMultiplierAtTen")]
    public float IntelligenceLatheMinimumTimeMultiplierAtTen = 0.50f;

    // Agility: movement and general action speed.
    [DataField("agilityMovementSpeedMultiplierPerPoint")]
    public float AgilityMovementSpeedMultiplierPerPoint = 0.004f;

    [DataField("agilityMovementSpeedPenaltyAtOne")]
    public float AgilityMovementSpeedPenaltyAtOne = 0.075f;

    [DataField("agilityMovementSpeedBonusAtTen")]
    public float AgilityMovementSpeedBonusAtTen = 0.075f;

    [DataField("agilityActionDelayPenaltyAtOne")]
    public float AgilityActionDelayPenaltyAtOne = 0.08f;

    [DataField("agilityActionDelayReductionAtTen")]
    public float AgilityActionDelayReductionAtTen = 0.10f;

    // Luck: critical hits and chance-based reward hooks.
    [DataField("luckCriticalChancePerPoint")]
    public float LuckCriticalChancePerPoint = 0.005f;

    [DataField("luckSingleShotCriticalChanceAtTen")]
    public float LuckSingleShotCriticalChanceAtTen = 0.25f;

    [DataField("luckCriticalDamageMultiplier")]
    public float LuckCriticalDamageMultiplier = 1.5f;

    [DataField("luckLootChancePerPoint")]
    public float LuckLootChancePerPoint = 0.025f;
}
