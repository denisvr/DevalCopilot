namespace DevalCopilot.Application.Features.Projects.Queries.GetGitCheckpointDiff;

/// <summary>
/// <see cref="ComparisonText"/> is the host comparison of the tracked paths that could be compared, in ordinal path order, each as one
/// whole file block. Every tracked changed path is accounted for exactly once: <see cref="ComparedPathCount"/> are in the text and
/// the remaining <see cref="Omissions"/> carry a fixed reason. <see cref="IsComplete"/> concerns the coverage of displayed tracked
/// content only (no omission); it says nothing about Git metadata, modes, renames, review applicability or approval. A capture
/// with no tracked change is complete with empty text. <see cref="Limitation"/> is the fixed statement of what the comparison is not.
/// </summary>
public sealed record GetGitCheckpointDiffQueryResult(
    string FingerprintSha256,
    string ComparisonText,
    bool IsComplete,
    int TrackedPathCount,
    int ComparedPathCount,
    string Limitation,
    IReadOnlyList<GetGitCheckpointDiffOmission> Omissions);
