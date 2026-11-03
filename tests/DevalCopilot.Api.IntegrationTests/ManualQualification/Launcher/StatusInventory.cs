namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The whole inventory of lot A: exactly one <c>claude auth status</c> start per context, of the one verified Claude executable and no
/// other argument, in the entry's own new working directory, with the stated deadline, no standard input and no output sink. The
/// filtered context may carry only the three environment names the production builder produces; the ordinary context is the launching
/// terminal's own environment by design and its names are not constrained (its values are never read or kept). A model, version,
/// help, login, logout, setup or any other command, a second start of either context, or any other executable is unlisted and
/// refused before its process exists.
/// </summary>
public static class StatusInventory
{
    public const int Total = 2;

    private static readonly string[] FilteredEnvironment = ["USERPROFILE", "CLAUDE_CONFIG_DIR", "SystemRoot"];

    public static IReadOnlyList<InventoryEntry> Create(string claudeExecutablePath, StatusContext context, TimeSpan execution) =>
    [
        new("claude.status.ordinary", 1, shape => IsStatus(shape, claudeExecutablePath, context, execution)
            && context.Current == ClaudeStatusObservation.Ordinary),
        new("claude.status.filtered", 1, shape => IsStatus(shape, claudeExecutablePath, context, execution)
            && context.Current == ClaudeStatusObservation.Filtered
            && shape.EnvironmentNamesWithin(FilteredEnvironment)),
    ];

    private static bool IsStatus(ProcessShape shape, string claudeExecutablePath, StatusContext context, TimeSpan execution) =>
        string.Equals(shape.ExecutablePath, claudeExecutablePath, StringComparison.OrdinalIgnoreCase)
        && shape.ArgumentsAre("auth", "status")
        && context.WorkingDirectory is { } directory
        && string.Equals(shape.WorkingDirectory, directory, StringComparison.OrdinalIgnoreCase)
        && string.Equals(shape.ApprovedRoot, directory, StringComparison.OrdinalIgnoreCase)
        && shape.TimeoutWithin(execution)
        && shape.HasStandardInput == false
        && shape.StandardOutputSinkPath is null
        && shape.StandardErrorSinkPath is null;
}
