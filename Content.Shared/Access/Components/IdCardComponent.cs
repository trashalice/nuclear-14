using Content.Shared.Access.Systems;
using Content.Shared.Roles;
using Content.Shared.PDA;
using Content.Shared.StatusIcon;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Access.Components;

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
[Access(typeof(SharedIdCardSystem), typeof(SharedPdaSystem), typeof(SharedAgentIdCardSystem), Other = AccessPermissions.ReadWrite)]
public sealed partial class IdCardComponent : Component
{
    [DataField]
    [AutoNetworkedField]
    // FIXME Friends
    public string? FullName;

    [DataField]
    [AutoNetworkedField]
    [Access(typeof(SharedIdCardSystem), typeof(SharedPdaSystem), typeof(SharedAgentIdCardSystem), Other = AccessPermissions.ReadWrite)]
    public LocId? JobTitle;

    private string? _jobTitle;

    [Access(typeof(SharedIdCardSystem), typeof(SharedPdaSystem), typeof(SharedAgentIdCardSystem), Other = AccessPermissions.ReadWriteExecute)]
    // #Misfits Change /Fix/: guard against malformed whitespace LocIds on map-placed IDs so access logging
    // and door interactions do not emit Unknown messageId warnings when a card job title is blank.
    public string? LocalizedJobTitle
    {
        set => _jobTitle = value;
        get
        {
            if (_jobTitle != null)
                return _jobTitle;

            if (string.IsNullOrWhiteSpace(JobTitle))
                return null;

            return Loc.GetString(JobTitle);
        }
    }

    /// <summary>
    /// The state of the job icon rsi.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public ProtoId<JobIconPrototype> JobIcon = "JobIconUnknown";

    /// <summary>
    /// The unlocalized names of the departments associated with the job
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public List<LocId> JobDepartments = new();

    [DataField]
    [Access(typeof(SharedIdCardSystem), typeof(SharedPdaSystem), typeof(SharedAgentIdCardSystem), Other = AccessPermissions.ReadWrite)]
    public ProtoId<JobPrototype>? JobPrototype;

    /// <summary>
    /// Determines if accesses from this card should be logged by <see cref="AccessReaderComponent"/>
    /// </summary>
    [DataField]
    public bool BypassLogging;

    [DataField]
    public LocId NameLocId = "access-id-card-component-owner-name-job-title-text";

    [DataField]
    public LocId FullNameLocId = "access-id-card-component-owner-full-name-job-title-text";

    [DataField]
    public bool CanMicrowave = true;
}
