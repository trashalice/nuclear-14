# SPECIAL tuning

Base values live on `HumanoidCharacterProfile.Special` and are copied to `SpecialComponent` when the character spawns.
The persistent player-data row mirrors these values for character info/history, but character setup remains authoritative.
Runtime systems should query `SharedSpecialSystem` instead of reading fields directly:

- `GetBase(entity, stat)` for character-creation values.
- `GetModifier(entity, stat)` for temporary modifier totals.
- `GetEffective(entity, stat)` for gameplay-safe values clamped to 1-10.
- `GetCurvedEffectDelta(entity, stat)` for gameplay effects that should scale non-linearly around 5.
- `GetCurvedEffectModifier(entity, stat, multiplierPerPoint)` for effects that multiply that curved delta by a tuning value.
- `HasRequirement(entity, stat, minimum)` for perks, weapons, or future skill gates.
- `TryModifyTemporary(entity, stat, modifier, duration, source)` for drugs, chems, injuries, perks, or equipment.

Balance values are in `Resources/Prototypes/_Misfits/Special/special_tuning.yml`.
Initial effects are deliberately small because SS14 combat is real-time.
Most gameplay effects use a curved delta from the effective stat instead of a flat point-for-point delta:

- 1: -5
- 2: -3.5
- 3: -2.25
- 4: -1
- 5: 0
- 6: +1
- 7: +2.25
- 8: +3.75
- 9: +5.5
- 10: +7.5

The tuning values below are multiplied by that curved delta:

- Strength changes held melee damage by `strengthMeleeDamageMultiplierPerPoint` and unarmed damage by the smaller `strengthUnarmedDamageMultiplierPerPoint`.
- Perception changes ranged spread/recoil with `perceptionSpreadMultiplierPerPoint`, heavy gun spread/recoil with `perceptionHeavyGunMultiplierPerPoint`, mine trigger delay with `perceptionMineDelayMultiplierPerPoint`, and fire spread delay with `perceptionFireDelayMultiplierPerPoint`.
- Endurance changes health thresholds with `enduranceHealthModifierPerPoint`, need decay with `enduranceNeedDecayMultiplierPerPoint`, stamina recovery with `enduranceStaminaRecoveryMultiplierPerPoint`, and poison/toxin damage with `enduranceToxinDamageMultiplierPerPoint`.
- Charisma changes character-creation loadout points by the curved delta times 2, rounded away from zero. Below 5 charisma, examine text gives a social tell; at 1-2 charisma, speech can gain light awkward phrasing.
- Intelligence changes crafting delay on a fixed curve: 1 blocks hand crafting, 5 is normal speed, and 10 is 50% faster for hand crafting. Lathe production uses `intelligenceLatheTimeMultiplierPerPoint`, material discounts use `intelligenceLatheMaterialUseMultiplierPerPoint`, and 10 intelligence grants the med HUD effect. At 1 intelligence, the low-intelligence accent is enabled.
- Agility changes movement speed with `agilityMovementSpeedMultiplierPerPoint` and action delay with `agilityActionDelayMultiplierPerPoint`.
- Luck changes critical-hit and lucky-scavenge chance. At 1 luck, clumsy uses its normal failure chance; at 2-4 luck, clumsy is still possible but much rarer.
