namespace DevalCopilot.Application.Features.Runs.Commands.RegisterAgentSupervision;

/// <summary>The observation's identity and revision, and whether this call only replayed an identical earlier registration.</summary>
public sealed record RegisterAgentSupervisionCommandResult(Guid SupervisionId, long Revision, bool Replayed);
