using System.Text.Json;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.AgentSupervisions;

/// <summary>The bounded run events of the material transitions of an Agent supervision observation (ADR-0056). Payloads carry identities,
/// the phase and a fixed reason code only; never an objective, a path, a process identifier, an argument or provider output. Registration,
/// each phase entry, the first attention and every actual closure append exactly one each, in the same transaction as the receipt change;
/// a heartbeat appends no event. SignalR stays a post-commit hint.</summary>
internal static class AgentSupervisionEvents
{
    public static RunEvent Registered(AgentSupervision receipt, DateTimeOffset nowUtc) => Record(
        receipt,
        RunEventType.AgentSupervisionRegistered,
        new
        {
            supervisionId = receipt.Id,
            attemptId = receipt.AttemptId,
            phase = receipt.Phase.ToString(),
        },
        nowUtc);

    public static RunEvent PhaseEntered(AgentSupervision receipt, DateTimeOffset nowUtc) => Record(
        receipt,
        RunEventType.AgentSupervisionPhaseEntered,
        new
        {
            supervisionId = receipt.Id,
            attemptId = receipt.AttemptId,
            phase = receipt.Phase.ToString(),
        },
        nowUtc);

    public static RunEvent Attention(AgentSupervision receipt, DateTimeOffset nowUtc) => Record(
        receipt,
        RunEventType.AgentSupervisionAttention,
        new
        {
            supervisionId = receipt.Id,
            attemptId = receipt.AttemptId,
            phase = receipt.Phase.ToString(),
            reason = receipt.AttentionReason,
        },
        nowUtc);

    public static RunEvent Closed(AgentSupervision receipt, DateTimeOffset nowUtc) => Record(
        receipt,
        RunEventType.AgentSupervisionClosed,
        new
        {
            supervisionId = receipt.Id,
            attemptId = receipt.AttemptId,
            phase = receipt.Phase.ToString(),
            reason = receipt.ClosureReason,
        },
        nowUtc);

    private static RunEvent Record(AgentSupervision receipt, string eventType, object payload, DateTimeOffset nowUtc) =>
        RunEvent.Record(
            Guid.NewGuid(), receipt.RunId, receipt.AttemptId, eventType, ParticipantIdentity.ForOrchestrator(),
            JsonSerializer.Serialize(payload), nowUtc);
}
