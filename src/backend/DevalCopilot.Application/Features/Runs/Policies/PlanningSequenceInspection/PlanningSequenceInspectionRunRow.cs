namespace DevalCopilot.Application.Features.Runs.Policies.PlanningSequenceInspection;

/// <summary>The run's project as its raw stored bytes, and the exact stored form of its execution mode, which only the Domain's own
/// storage reader classifies.</summary>
internal sealed record PlanningSequenceInspectionRunRow(byte[]? ProjectId, string? StoredMode);
