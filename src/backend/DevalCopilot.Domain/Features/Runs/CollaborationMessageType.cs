namespace DevalCopilot.Domain.Features.Runs;

/// <summary>The closed, versioned vocabulary for durable collaboration facts.</summary>
public enum CollaborationMessageType
{
    Proposal = 0,
    Acceptance = 1,
    Challenge = 2,
    Question = 3,
    Decision = 4,
    ExecutionReport = 5,
    ReviewFinding = 6,
    RevisionResponse = 7,
    Escalation = 8,
}
