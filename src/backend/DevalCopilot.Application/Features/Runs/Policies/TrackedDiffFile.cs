namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>One <c>diff --git</c> block of a captured tracked diff. <see cref="Header"/> is the exact text before
/// the first hunk; every element of <see cref="Hunks"/> is one exact, validated, complete hunk. For a kind other
/// than <see cref="TrackedDiffFileKind.Text"/> there are no hunks, and <see cref="Reason"/> is a fixed value
/// (<c>binary</c>, <c>unsupported_format</c>, <c>malformed_hunk</c>, or <c>header_unparseable</c>).</summary>
internal sealed record TrackedDiffFile(
    string? Path, TrackedDiffFileKind Kind, string Header, IReadOnlyList<string> Hunks, string? Reason);
