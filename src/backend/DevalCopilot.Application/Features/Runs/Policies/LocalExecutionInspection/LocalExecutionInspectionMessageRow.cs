namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

/// <summary>The identity, run and database-classified type of one named message. No text, summary or structured content is selected.</summary>
internal sealed record LocalExecutionInspectionMessageRow(Guid Id, Guid RunId, bool IsProposal, bool IsExecutionReport);
