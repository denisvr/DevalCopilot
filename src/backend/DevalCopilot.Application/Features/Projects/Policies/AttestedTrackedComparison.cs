using System.Text;
using DevalCopilot.Application.Features.Projects.Ports;

namespace DevalCopilot.Application.Features.Projects.Policies;

/// <summary>
/// The source admission and host comparison that every consumer of attested tracked sources shares (ADR-0024, ADR-0027). It is
/// re-derived from a capture's own changed paths and the reader's immutable <see cref="GitWorkspaceTrackedFile"/> facts, and
/// nothing here accepts, repairs or falls back to a Git patch. Every tracked changed path becomes exactly one
/// <see cref="Entry"/> in ordinal path order: either the <see cref="TrackedComparison"/> block of its attested before/after text
/// or one fixed omission reason. Missing facts, facts for a path the capture did not report as a tracked change, facts that
/// contradict the porcelain state, facts outside the source bounds, duplicated facts and a spent retained-source budget are each an
/// omission or an ignored claim, never delivered text. A reader (or double) that returned no attestation therefore delivers none.
/// What a consumer then fits into its own surface (the Agent manifest ceiling, the inspection response limit) is its own policy.
/// </summary>
internal static class AttestedTrackedComparison
{
    /// <summary>The outcome for one tracked changed path: <see cref="Block"/> when it can be compared, otherwise a fixed
    /// <see cref="Reason"/>.</summary>
    internal sealed record Entry(string Path, string? Block, string? Reason);

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    internal static IReadOnlyList<Entry> Derive(
        IReadOnlyList<GitWorkspaceChangedPath> changedPaths,
        IReadOnlyList<GitWorkspaceTrackedFile>? facts,
        TrackedSourcePurpose purpose)
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

        var entries = new List<Entry>(tracked.Length);
        long retained = 0;
        foreach (var group in tracked)
        {
            var path = group.Key;
            var reason = Resolve(
                path, group.ToArray(), untrackedPaths.Contains(path), factsByPath, purpose, ref retained, out var block);
            entries.Add(new Entry(path, reason is null ? block : null, reason));
        }

        return entries;
    }

    /// <summary>The fixed omission reason for one tracked path, or null with its comparison block when it is deliverable.</summary>
    private static string? Resolve(
        string path,
        GitWorkspaceChangedPath[] states,
        bool alsoUntracked,
        Dictionary<string, GitWorkspaceTrackedFile[]> factsByPath,
        TrackedSourcePurpose purpose,
        ref long retained,
        out string? block)
    {
        block = null;
        if (purpose == TrackedSourcePurpose.AgentDelivery && GitWorkspaceInstructionContext.IsReservedPath(path))
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
