namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentSupervision;

/// <summary>The effective attention (the recorded first attention when one exists, otherwise the one the policy derives now) and the
/// recorded one when it is already durable. A derived attention not yet persisted has no recorded reason.</summary>
public sealed record AgentSupervisionAttentionView(
    string EffectiveReason, DateTimeOffset EffectiveAtUtc, string? RecordedReason, DateTimeOffset? RecordedAtUtc);
