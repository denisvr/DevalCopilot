namespace DevalCopilot.Api.Features.Runs.GetAgentSupervision;

/// <summary>The effective attention with its fixed reason text, and the recorded one when it is already durable (<c>RecordedReasonCode</c>
/// and <c>RecordedAtUtc</c> are present together; absent means the attention is derived now and not yet persisted). Attention proves
/// only that the host's own observation missed a deadline or ended; earlier authorized work may still finish later.</summary>
public sealed record AgentSupervisionAttentionResponse(
    string EffectiveReasonCode,
    string EffectiveReasonText,
    string EffectiveAtUtc,
    string? RecordedReasonCode,
    string? RecordedAtUtc);
