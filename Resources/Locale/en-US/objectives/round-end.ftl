objectives-round-end-result = {$count ->
    [one] There was one {$agent}.
    *[other] There were {$count} {MAKEPLURAL($agent)}.
}

objectives-round-end-result-in-custody = {$custody} out of {$count} {MAKEPLURAL($agent)} were in custody.

objectives-player-user-named = [color=White]{$name}[/color] ([color=gray]{$user}[/color])
objectives-player-named = [color=White]{$name}[/color]

objectives-no-objectives = {$custody}{$title} was a {$agent}.
objectives-with-objectives = {$custody}{$title} was a {$agent} who had the following objectives:

objectives-objective-success = {$objective} | [color={$markupColor}]Success![/color]
objectives-objective-fail = {$objective} | [color={$markupColor}]Failure![/color] ({$progress}%)

objectives-in-custody = [bold][color=red]| IN CUSTODY | [/color][/bold]

#Misfits Change
objective-issuer-ncr = [color=#cc2f2f]NCR[/color]
objective-issuer-brotherhoodofsteel = [color=#4f81bd]Brotherhood of Steel[/color]
# #Misfits Change - Caesar's Legion issuer label for the C character menu objectives panel
objective-issuer-caesarlegion = [color=#8B0000]Caesar's Legion[/color]
objective-issuer-geometerofblood = [color=#b22222]The Geometer of Blood[/color]
# #Misfits Add - issuer labels for Vault and Town factions
objective-issuer-vault = [color=#FFD700]Vault[/color]
objective-issuer-townsfolk = [color=#8FBC8F]Town[/color]
# #Misfits Add - issuer labels for Robots, FEV Mutants, and Raiders
objective-issuer-playerrobot = [color=#607d8b]Robots[/color]
objective-issuer-playersupermutant = [color=#6b8e23]FEV Mutants[/color]
objective-issuer-playerraider = [color=#c0522a]Raiders[/color]
