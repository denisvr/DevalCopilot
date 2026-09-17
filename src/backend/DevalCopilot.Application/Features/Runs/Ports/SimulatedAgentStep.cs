using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>
/// One canned step of the deterministic simulated-agent-collaboration sequence.
/// A real Codex/Claude Code adapter later replaces this port; the hosted supervisor
/// and the commands that record steps do not change shape when that happens.
/// </summary>
public sealed record SimulatedAgentStep(
    RunStage Stage,
    ParticipantKind Actor,
    ParticipantKind Recipient,
    string EventType,
    CollaborationMessageType MessageType,
    int? InReplyToStepIndex,
    string Summary,
    string StructuredContentJson);
