namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>What the production target resolver selected for one capability: its kind and, for a launch, the executable and (for a
/// script launch) the script. The paths are compared with the approved manifest only and are never printed or stored.</summary>
public sealed record LaunchResolution(LaunchResolutionKind Kind, string? ExecutablePath = null, string? ScriptPath = null)
{
    public static LaunchResolution Unavailable { get; } = new(LaunchResolutionKind.Unavailable);
}
