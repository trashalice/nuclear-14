// #Misfits Change - Client-side command to open mentor help
using Content.Client._Misfits.UserInterface.Systems.MentorHelp;
using Content.Shared.Administration;
using Robust.Client.UserInterface;
using Robust.Shared.Console;
using Robust.Shared.Network;

namespace Content.Client._Misfits.Commands;

[AnyCommand]
public sealed class OpenMentorHelpCommand : LocalizedCommands
{
    [Dependency] private readonly IUserInterfaceManager _userInterfaceManager = default!;

    public override string Command => "openmentorhelp";

    public override string Help => "Usage: openmentorhelp [user guid]";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        shell.WriteLine("Mentor help has been retired.");
    }
}
