# #Misfits Change
# Ghoul Reversal (De-Ghoulification) Syringe Localization

# Private message shown only to the player being reversed.
ghoul-reversal-self = You feel a strange warmth spreading through your veins as the compound begins to work. The radiation damage starts to reverse... you're becoming human again!
# Emote broadcast to bystanders (no $target — emote system prefixes the entity name).
ghoul-reversal-others = begins transforming — their ghoulish features fade as their skin returns to human form
ghoul-reversal-not-ghoul = They don't appear to be a ghoul. This serum has no effect on them.

# Reagent (Promethine) strings
# Private feedback to the affected player.
ghoul-reversal-reagent-self = The Promethine floods your cells — the radiation markers begin to dissolve! You can feel yourself returning to normal...
# Emote broadcast to bystanders.
ghoul-reversal-reagent-others = shudders as the Promethine takes hold — their ghoulish appearance slowly receding
ghoul-reversal-reagent-too-old = The Promethine has no effect. The ghoulification markers are too deeply set to be reversed by chemistry.

# Radiation death ghoulification
# Keys used by GhoulifyOnRadiationDeathSystem (note: ghoul-on-death-* kept below for reference).
# Private message shown only to the newly-ghoulified player.
ghoulify-on-death-self = The fatal dose of radiation tears through your body — but instead of killing you, it transforms you. You are a ghoul now.
# Emote broadcast to bystanders (no $target — emote system prefixes the entity name).
ghoulify-on-death-others = collapses from radiation... then rises again, skin twisted and eyes hollow — now a ghoul

# Legacy keys (ghoul-on-death-*) — kept for reference; system now uses ghoulify-on-death-* keys above.
#ghoul-on-death-self = The fatal dose of radiation tears through your body — but instead of killing you, it transforms you. You are a ghoul now.
#ghoul-on-death-others = {THE($target)} collapses from the radiation... but rises again, skin twisted and eyes hollow. They've become a ghoul!

# Reagent guidebook
reagent-effect-guidebook-ghoul-reversal = reverse ghoulification if administered within 12 hours of exposure ({ $chance ->
  [1] always
  *[other] { $chance } chance
})

# Promethine reagent strings
reagent-name-promethine = Promethine
reagent-desc-promethine = An extraordinarily rare compound synthesized from RadAway, RadX, and cellular catalysts. Clinical studies suggest it can suppress and reverse the FEV radiation cascade responsible for ghoulification — but only within a narrow window after initial exposure. After 12 hours, the cellular mutation becomes permanent.
reagent-physical-desc-luminous = luminous golden

