# SPECIAL tuning

Base values live on `HumanoidCharacterProfile.Special` and are copied to `SpecialComponent` when the character spawns.
The persistent player-data row mirrors these values for character info/history, but character setup remains authoritative.
Runtime systems should query `SharedSpecialSystem` instead of reading fields directly:

- `GetBase(entity, stat)` for character-creation values.
- `GetModifier(entity, stat)` for temporary modifier totals.
- `GetEffective(entity, stat)` for gameplay-safe values clamped to 1-10.
- `GetCurvedEffectDelta(entity, stat)` for gameplay effects that should scale non-linearly around 5.
- `GetCurvedEffectScale(entity, stat, valueAtOne, valueAtTen)` for effects that should hit exact endpoints at 1 and 10.
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

The tuning values below are multiplied by that curved delta or scaled to explicit 1/10 endpoints:

- Strength changes melee damage by `strengthMeleeDamageMultiplierPerPoint`.
- Perception changes ranged spread/recoil from `perceptionSpreadPenaltyAtOne` at 1 PER to `perceptionSpreadReductionAtTen` at 10 PER.
- Endurance changes health thresholds from `enduranceHealthPenaltyAtOne` at 1 END to `enduranceHealthBonusAtTen` at 10 END.
- Charisma changes character-creation loadout points by the curved delta times 2, rounded away from zero.
- Intelligence changes crafting delay on a fixed curve: 1 blocks hand crafting, 5 is normal speed, and 10 is 50% faster for hand crafting. Lathe production is instant at 10 intelligence. At 1 intelligence, the low-intelligence accent is enabled.
- Agility changes movement speed from `agilityMovementSpeedPenaltyAtOne` at 1 AGI to `agilityMovementSpeedBonusAtTen` at 10 AGI.
- Luck changes critical-hit and lucky-scavenge chance. Below 4 luck, clumsy is applied until luck recovers.
