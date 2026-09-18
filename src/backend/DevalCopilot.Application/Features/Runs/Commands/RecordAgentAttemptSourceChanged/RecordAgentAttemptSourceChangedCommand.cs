using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptSourceChanged;

/// <summary>
/// Records that an Agent attempt's source drifted before the provider was ever invoked — a
/// pre-dispatch fresh evidence recapture found a fingerprint that no longer matches the
/// checkpoint this attempt committed to. No Proposal is ever appended for this outcome.
/// </summary>
public sealed record RecordAgentAttemptSourceChangedCommand(Guid RunId, Guid AttemptId) : ICommand<Result>;
