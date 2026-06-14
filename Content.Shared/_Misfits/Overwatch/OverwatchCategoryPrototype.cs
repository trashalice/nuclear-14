using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.Overwatch;

[Prototype]
public sealed partial class OverwatchCategoryPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = string.Empty;

    [DataField(required: true)]
    public string Name { get; private set; } = string.Empty;

    [DataField]
    public int SortOrder { get; private set; }

    public string LocalizedName => Loc.GetString(Name);
}
