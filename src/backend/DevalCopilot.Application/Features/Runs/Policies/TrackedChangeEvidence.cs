using System.Text;
using DevalCopilot.Application.Features.Projects.Policies;
using DevalCopilot.Application.Features.Projects.Ports;

namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>
/// The tracked-file evidence a new Agent manifest may deliver, re-derived from a reader's attested facts and the capture's own
/// changed paths (ADR-0024). Nothing here accepts, repairs or falls back to a Git patch: <see cref="Text"/> is the host comparison
/// (<see cref="TrackedComparison"/>) of the attested before/after text of the files that survive every check of <see cref="AttestedTrackedComparison"/>, in ordinal
/// path order, and every other tracked changed path is in <see cref="Omissions"/> with one fixed reason. Missing facts, facts for
/// a path the capture did not report as a tracked change, facts that contradict the porcelain state, facts outside the source
/// bounds, duplicated facts and a spent retained-source budget are each an omission or an ignored claim, never delivered text.
/// A reader (or double) that returned no attestation at all therefore delivers no tracked text.
/// </summary>
internal sealed class TrackedChangeEvidence
{
    internal sealed record Omission(string Path, string Reason);

    /// <summary>Composed deliverable comparison text, or null when no tracked evidence was provided at all (a manifest built
    /// without one keeps its historical "not provided" meaning).</summary>
    internal string? Text { get; }

    /// <summary>Every tracked changed path that has no comparison text, each exactly once with a fixed reason, in ordinal path order.</summary>
    internal IReadOnlyList<Omission> Omissions { get; }

    internal TrackedChangeEvidence(string? text, IReadOnlyList<Omission> omissions)
    {
        Text = text;
        Omissions = omissions;
    }

    /// <summary>No tracked change evidence: the historical "not provided" form.</summary>
    internal static TrackedChangeEvidence NotProvided { get; } = new(null, []);

    internal static TrackedChangeEvidence From(GitWorkspaceEvidenceResult evidence) =>
        Derive(evidence.ChangedPaths, evidence.TrackedFiles);

    internal static TrackedChangeEvidence Derive(
        IReadOnlyList<GitWorkspaceChangedPath> changedPaths, IReadOnlyList<GitWorkspaceTrackedFile>? facts)
    {
        var text = new StringBuilder();
        var omissions = new List<Omission>();
        foreach (var entry in AttestedTrackedComparison.Derive(changedPaths, facts, TrackedSourcePurpose.AgentDelivery))
        {
            if (entry.Reason is { } reason)
            {
                omissions.Add(new Omission(entry.Path, reason));
            }
            else
            {
                text.Append(entry.Block);
            }
        }

        return new TrackedChangeEvidence(text.ToString(), omissions);
    }
}
