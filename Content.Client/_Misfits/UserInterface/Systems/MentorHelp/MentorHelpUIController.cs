using System;
using System.Diagnostics.CodeAnalysis;
using Content.Client.Gameplay;
using Content.Client.Lobby;
using Content.Client._Misfits.Administration.Systems;
using Content.Shared._Misfits.Administration;
using JetBrains.Annotations;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Network;

namespace Content.Client._Misfits.UserInterface.Systems.MentorHelp;

[UsedImplicitly]
public sealed class MentorHelpUIController : UIController,
    IOnSystemChanged<MentorHelpSystem>,
    IOnStateChanged<GameplayState>,
    IOnStateChanged<LobbyState>
{
    public const string MHelpReceiveSound = "/Audio/Admin/adminhelp_old.ogg";
    public const string MHelpSendSound = "/Audio/Admin/adminhelp_old.ogg";
    public const string MHelpErrorSound = "/Audio/Admin/ahelp_error.ogg";

    public IMentorHelpUIHandler? UIHelper;

    public override void Initialize()
    {
        base.Initialize();
    }

    public void OnSystemLoaded(MentorHelpSystem system)
    {
    }

    public void OnSystemUnloaded(MentorHelpSystem system)
    {
    }

    public void OnStateEntered(GameplayState state)
    {
    }

    public void OnStateExited(GameplayState state)
    {
    }

    public void OnStateEntered(LobbyState state)
    {
    }

    public void OnStateExited(LobbyState state)
    {
    }

    public void UnloadButton()
    {
    }

    public void LoadButton()
    {
    }

    public void Open()
    {
    }

    public void Open(NetUserId userId)
    {
    }

    public void ToggleWindow()
    {
    }

    public void PopOut()
    {
    }

    public void EnsureUIHelper()
    {
        UIHelper ??= new AdminMentorHelpUIHandler(default);
    }
}

public interface IMentorHelpUIHandler : IDisposable
{
    bool IsMentor { get; }
    bool IsOpen { get; }
    void Receive(SharedMentorHelpSystem.MentorHelpTextMessage message);
    void Close();
    void Open(NetUserId netUserId);
    void ToggleWindow();
    void PeopleTypingUpdated(MentorHelpPlayerTypingUpdated args);
    void ClearAllPanels();
    event Action? OnClose;
    event Action? OnOpen;
    Action<NetUserId, string, bool>? SendMessageAction { get; set; }
    event Action<NetUserId, string>? InputTextChanged;
}

public sealed class AdminMentorHelpUIHandler : IMentorHelpUIHandler
{
    public AdminMentorHelpUIHandler(NetUserId owner)
    {
    }

    public Content.Client._Misfits.Administration.UI.MentorHelp.MentorHelpControl? Control { get; set; }

    public bool IsMentor => true;
    public bool IsOpen => false;
    public bool EverOpened;

    public void Receive(SharedMentorHelpSystem.MentorHelpTextMessage message)
    {
    }

    public void Close()
    {
    }

    public void Open(NetUserId netUserId)
    {
    }

    public void ToggleWindow()
    {
    }

    public void PeopleTypingUpdated(MentorHelpPlayerTypingUpdated args)
    {
    }

    public void ClearAllPanels()
    {
    }

    public void HideAllPanels()
    {
    }

    public bool TryGetChannel(NetUserId ch, [NotNullWhen(true)] out Content.Client._Misfits.Administration.UI.MentorHelp.MentorHelpPanel? panel)
    {
        panel = null;
        return false;
    }

    public Content.Client._Misfits.Administration.UI.MentorHelp.MentorHelpPanel EnsurePanel(NetUserId channelId)
    {
        return null!;
    }

    public event Action? OnClose;
    public event Action? OnOpen;
    public Action<NetUserId, string, bool>? SendMessageAction { get; set; }
    public event Action<NetUserId, string>? InputTextChanged
    {
        add { }
        remove { }
    }

    public void Dispose()
    {
    }
}

public sealed class UserMentorHelpUIHandler : IMentorHelpUIHandler
{
    public UserMentorHelpUIHandler(NetUserId owner)
    {
    }

    public bool IsMentor => false;
    public bool IsOpen => false;

    public void Receive(SharedMentorHelpSystem.MentorHelpTextMessage message)
    {
    }

    public void Close()
    {
    }

    public void Open(NetUserId netUserId)
    {
    }

    public void ToggleWindow()
    {
    }

    public void PeopleTypingUpdated(MentorHelpPlayerTypingUpdated args)
    {
    }

    public void ClearAllPanels()
    {
    }

    public event Action? OnClose;
    public event Action? OnOpen;
    public Action<NetUserId, string, bool>? SendMessageAction { get; set; }
    public event Action<NetUserId, string>? InputTextChanged
    {
        add { }
        remove { }
    }

    public void Dispose()
    {
    }
}
