namespace DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;

/// <summary>The immutable direct-guidance snapshot a caller expects the attempt to carry at the dispatch commit
/// (<see langword="null"/> text means none was recorded). Supplied by the two mutation supervisors from their
/// eligibility feed so that a stale projection can never confer dispatch authority: the fresh persisted value must
/// equal it exactly.</summary>
public sealed record ExpectedDirectHumanGuidance(string? Text);
