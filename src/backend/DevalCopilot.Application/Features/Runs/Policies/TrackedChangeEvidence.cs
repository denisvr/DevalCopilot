using System.Text;
using DevalCopilot.Application.Features.Projects.Policies;
using DevalCopilot.Application.Features.Projects.Ports;

namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>
/// The tracked-file evidence a new Agent manifest may deliver, re-derived from a reader's attested facts and the capture's own
/// changed paths (ADR-0024). Nothing here accepts, repairs or falls back to a Git patch: <see cref="Text"/> is the host comparison
/// (<see cref="TrackedComparison"/>) of the attested before/after text of the files that survive every check below, in ordinal
/// path order, and every other tracked changed path is in <see cref="Omissions"/> with one fixed reason. Missing facts, facts for
/// a path the capture did not report as a tracked change, facts that contradict the porcelain state, facts outside the source
/// bounds, duplicated facts and a spent retained-source budget are each an omission or an ignored claim, never delivered text.
/// A reader (or double) that returned no attestation at all therefore delivers no tracked text.
/// </summary>
internal sealed class TrackedChangeEvidence
{
    internal sealed record Omission(string Path, string Reason);

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

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
        var tracked = changedPaths
            .Where(path => !(path.IndexStatus == "?" && path.WorkTreeStatus == "?"))
            .GroupBy(path => path.Path, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();
        var untrackedPaths = changedPaths
            .Where(path => path.IndexStatus == "?" && path.WorkTreeStatus == "?")
            .Select(path => path.Path)
            .ToHashSet(StringComparer.Ordinal);
        var factsByPath = (facts ?? [])
            .GroupBy(fact => fact.Path, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var text = new StringBuilder();
        var omissions = new List<Omission>();
        long retained = 0;
        foreach (var group in tracked)
        {
            var path = group.Key;
            var reason = Resolve(path, group.ToArray(), untrackedPaths.Contains(path), factsByPath, ref retained, out var block);
            if (reason is not null)
            {
                omissions.Add(new Omission(path, reason));
            }
            else
            {
                text.Append(block);
            }
        }

        return new TrackedChangeEvidence(text.ToString(), omissions);
    }

    /// <summary>The fixed omission reason for one tracked path, or null with its comparison block when it is deliverable.</summary>
    private static string? Resolve(
        string path,
        GitWorkspaceChangedPath[] states,
        bool alsoUntracked,
        Dictionary<string, GitWorkspaceTrackedFile[]> factsByPath,
        ref long retained,
        out string? block)
    {
        block = null;
        if (GitWorkspaceInstructionContext.IsReservedPath(path))
        {
            return Reason(GitWorkspaceTrackedOmission.ReservedInstructionFile);
        }

        if (!factsByPath.TryGetValue(path, out var claimed))
        {
            return "not_attested";
        }

        if (claimed.Length != 1 || states.Length != 1)
        {
            return "attestation_incoherent";
        }

        var fact = claimed[0];
        var state = states[0];
        if (fact.Omission is { } omission)
        {
            return fact.BeforeText is null && fact.AfterText is null ? Reason(omission) : "attestation_incoherent";
        }

        // The statuses this host does not compare win over any text a reader claims for them.
        var expectation = GitWorkspaceTrackedStatus.Classify(state, alsoUntracked);
        if (expectation.Refusal is { } refusal)
        {
            return Reason(refusal);
        }

        // An absent side is claimed only where the status says the file is absent there, and never both.
        if ((fact.BeforeText is null && fact.AfterText is null)
            || (fact.BeforeText is null) == expectation.BaselineExpected
            || (fact.AfterText is null) == expectation.CurrentExpected
            || !TextIsWithinBounds(fact.BeforeText)
            || !TextIsWithinBounds(fact.AfterText))
        {
            return "attestation_incoherent";
        }

        var size = (long)Encoding.UTF8.GetByteCount(fact.BeforeText ?? string.Empty) + Encoding.UTF8.GetByteCount(fact.AfterText ?? string.Empty);
        if (retained + size > GitWorkspaceTrackedFile.MaxRetainedBytes)
        {
            return Reason(GitWorkspaceTrackedOmission.AggregateLimit);
        }

        if (!GitWorkspaceTrackedStatus.IsEncodablePath(path))
        {
            return Reason(GitWorkspaceTrackedOmission.UnencodablePath);
        }

        retained += size;
        block = TrackedComparison.Compose(path, fact.BeforeText, fact.AfterText);
        return block is null ? Reason(GitWorkspaceTrackedOmission.NoContentDifference) : null;
    }

    private static bool TextIsWithinBounds(string? text)
    {
        if (text is null)
        {
            return true;
        }

        if (text.Length > GitWorkspaceTrackedFile.MaxSourceBytes || text.Contains('\0'))
        {
            return false;
        }

        try
        {
            if (StrictUtf8.GetByteCount(text) > GitWorkspaceTrackedFile.MaxSourceBytes)
            {
                return false;
            }
        }
        catch (EncoderFallbackException)
        {
            return false;
        }

        return TrackedComparison.SplitLines(text).Count <= GitWorkspaceTrackedFile.MaxSourceLines;
    }

    internal static string Reason(GitWorkspaceTrackedOmission omission) => omission switch
    {
        GitWorkspaceTrackedOmission.ReservedInstructionFile => "reserved_instruction_file",
        GitWorkspaceTrackedOmission.UnsupportedStatus => "unsupported_status",
        GitWorkspaceTrackedOmission.Unmerged => "unmerged",
        GitWorkspaceTrackedOmission.UnsupportedMode => "unsupported_mode",
        GitWorkspaceTrackedOmission.SymbolicLink => "symbolic_link",
        GitWorkspaceTrackedOmission.Submodule => "submodule",
        GitWorkspaceTrackedOmission.NotRegularFile => "not_regular_file",
        GitWorkspaceTrackedOmission.ContainmentUnproven => "containment_unproven",
        GitWorkspaceTrackedOmission.Unreadable => "unreadable",
        GitWorkspaceTrackedOmission.TooLarge => "too_large",
        GitWorkspaceTrackedOmission.TooManyLines => "too_many_lines",
        GitWorkspaceTrackedOmission.Binary => "binary",
        GitWorkspaceTrackedOmission.InvalidUtf8 => "invalid_utf8",
        GitWorkspaceTrackedOmission.BaselineUnavailable => "baseline_unavailable",
        GitWorkspaceTrackedOmission.BaselineUnverified => "baseline_unverified",
        GitWorkspaceTrackedOmission.UnencodablePath => "unencodable_path",
        GitWorkspaceTrackedOmission.AggregateLimit => "aggregate_limit",
        GitWorkspaceTrackedOmission.NoContentDifference => "no_content_difference",
        _ => "not_attested",
    };
}
