namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

/// <summary>The one pinned recipe a selected verification step runs, read only for the exact comparison with its execution snapshot. The
/// executable and the arguments JSON are read only within their internal byte guards (128 KiB and 2 MiB) and flagged when oversized.</summary>
internal sealed record LocalExecutionInspectionSelectedRecipeRow(
    Guid VerificationCommandId,
    int? CommandNumber,
    string? Name,
    bool NameOversized,
    string? ExecutablePath,
    bool ExecutablePathOversized,
    string? ArgumentsJson,
    bool ArgumentsJsonOversized,
    int? TimeoutSeconds);
