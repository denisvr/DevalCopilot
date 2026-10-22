namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

/// <summary>The run's project and the exact stored form of its execution mode, which only the Domain's own storage reader classifies.</summary>
internal sealed record LocalExecutionInspectionRunRow(Guid Id, Guid ProjectId, string? StoredMode);
