namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

/// <summary>The scalar identity of one pinned recipe of the sequence: never its executable, arguments or path. <see cref="EnabledStored"/>
/// holds the stored bytes of the enabled flag, so only exactly one is enabled.</summary>
internal sealed record LocalExecutionInspectionRecipeRow(
    Guid VerificationCommandId, int? Ordinal, int? CommandNumber, string? Name, bool NameOversized, byte[]? EnabledStored);
