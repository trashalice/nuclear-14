// #Misfits Add - Server-definable UI color palette prototype, drives the swappable stylesheet theme.
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.UI;

/// <summary>
/// A selectable UI color palette. Servers can ship their own; the client stores the chosen id in the
/// <c>ui.theme_palette</c> CVar and rebuilds the Nano stylesheet from the resolved prototype. If the
/// saved id is not present on a server, the client falls back to its built-in default colors.
/// </summary>
[Prototype("misfitsUiTheme")]
public sealed partial class UiThemePrototype : IPrototype, IComparable<UiThemePrototype>
{
    [IdDataField]
    public string ID { get; private set; } = string.Empty;

    /// <summary>Fluent localization id shown in the options dropdown.</summary>
    [DataField("name", required: true)]
    public string Name { get; private set; } = string.Empty;

    /// <summary>Sort order within the dropdown (low to high).</summary>
    [DataField]
    public int Order;

    /// <summary>Primary accent: headings, dividers, borders.</summary>
    [DataField] public Color Accent = Color.FromHex("#33FF66");

    /// <summary>Secondary/dim text.</summary>
    [DataField] public Color AccentDim = Color.FromHex("#1E9C3D");

    /// <summary>Themed panel fill.</summary>
    [DataField] public Color PanelBg = Color.FromHex("#0C1F0E");

    [DataField] public Color ButtonDefault = Color.FromHex("#163E1E");
    [DataField] public Color ButtonHovered = Color.FromHex("#205A2C");
    [DataField] public Color ButtonPressed = Color.FromHex("#2E7D3F");
    [DataField] public Color ButtonDisabled = Color.FromHex("#10240F");

    public int CompareTo(UiThemePrototype? other)
    {
        return Order.CompareTo(other?.Order);
    }
}
