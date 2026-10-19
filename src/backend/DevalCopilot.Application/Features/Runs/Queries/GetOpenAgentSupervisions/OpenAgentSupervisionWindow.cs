namespace DevalCopilot.Application.Features.Runs.Queries.GetOpenAgentSupervisions;

/// <summary>The observations of one sweep round and the cursor for the next (null when none are open).</summary>
public sealed record OpenAgentSupervisionWindow(IReadOnlyList<Guid> SupervisionIds, Guid? NextCursor);
