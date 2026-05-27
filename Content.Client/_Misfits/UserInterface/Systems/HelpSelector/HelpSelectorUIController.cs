// #Misfits Change - UIController for the combined Help button in the top bar.
// Replaces the separate F1 (Mentor Help) and F2 (Admin Help) top bar buttons with a single
// popup selector that lets the player choose which help channel to open.
// F12 is bound to toggle this selector window.
using Content.Client._Misfits.UserInterface.Systems.MentorHelp;
using Content.Client.Gameplay;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Bwoink;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Shared.Input;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input.Binding;

namespace Content.Client._Misfits.UserInterface.Systems.HelpSelector;

[UsedImplicitly]
public sealed class HelpSelectorUIController : UIController, IOnStateChanged<GameplayState>
{
    // Resolves the combined Help button from the active top menu bar widget
    private MenuButton? GameHelpButton => UIManager.GetActiveUIWidgetOrNull<GameTopMenuBar>()?.HelpButton;

    private HelpSelectorWindow? _window;

    public void OnStateEntered(GameplayState state)
    {
        if (GameHelpButton != null)
        {
            // Avoid double-subscription on re-entry
            GameHelpButton.OnPressed -= HelpButtonPressed;
            GameHelpButton.OnPressed += HelpButtonPressed;
        }

        // #Misfits Change - Bind F12 to toggle the Help Selector popup
        CommandBinds.Builder
            .Bind(ContentKeyFunctions.OpenHelpSelector,
                InputCmdHandler.FromDelegate(_ => ToggleSelectorWindow()))
            .Register<HelpSelectorUIController>();
    }

    public void OnStateExited(GameplayState state)
    {
        if (GameHelpButton != null)
            GameHelpButton.OnPressed -= HelpButtonPressed;

        // Unregister F12 binding when leaving gameplay
        CommandBinds.Unregister<HelpSelectorUIController>();

        // Clean up the popup if the state changes (e.g. player returns to lobby)
        _window?.Close();
        _window = null;
    }

    private void HelpButtonPressed(BaseButton.ButtonEventArgs args)
    {
        ToggleSelectorWindow();
    }

    private void ToggleSelectorWindow()
    {
        if (_window == null)
        {
            _window = new HelpSelectorWindow();

            // "Admin Help" button — delegates to the existing AHelpUIController
            _window.AdminHelpButton.OnPressed += _ =>
            {
                _window.Close();
                UIManager.GetUIController<AHelpUIController>().ToggleWindow();
            };

            // "Mentor Help" button — delegates to the existing MentorHelpUIController
            // "Mentor Help" button — delegates to the existing MentorHelpUIController
            // _window.MentorHelpButton.OnPressed += _ =>
            // {
            //     _window.Close();
            //     UIManager.GetUIController<MentorHelpUIController>().ToggleWindow();
            // };

            // Null out reference when the window is closed so it can be reopened fresh
            _window.OnClose += () => _window = null;
        }

        if (_window.IsOpen)
            _window.Close();
        else
            _window.OpenCentered();
    }
}
