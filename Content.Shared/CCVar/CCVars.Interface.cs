using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    public static readonly CVarDef<string> UIClickSound =
        CVarDef.Create("interface.click_sound", "/Audio/UserInterface/click.ogg", CVar.REPLICATED);

    public static readonly CVarDef<string> UIHoverSound =
        CVarDef.Create("interface.hover_sound", "/Audio/UserInterface/hover.ogg", CVar.REPLICATED);

    public static readonly CVarDef<string> UILayout =
        CVarDef.Create("ui.layout", "Separated", CVar.CLIENTONLY | CVar.ARCHIVE);

    // #Misfits Add - Selected UI color theme id (see misfitsUiTheme prototypes). Drives the swappable
    // stylesheet palette chosen from the options menu; client-saved so it follows the player.
    public static readonly CVarDef<string> UiThemePalette =
        CVarDef.Create("ui.theme_palette", "pipboy_green", CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<string> OverlayScreenChatSize =
        CVarDef.Create("ui.overlay_chat_size", "", CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<string> DefaultScreenChatSize =
        CVarDef.Create("ui.default_chat_size", "", CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<string> SeparatedScreenChatSize =
        CVarDef.Create("ui.separated_chat_size", "0.6,0", CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<bool> OutlineEnabled =
        CVarDef.Create("outline.enabled", false, CVar.CLIENTONLY);
}
