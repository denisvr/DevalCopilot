namespace DevalCopilot.Api.Features.Projects.GetGitCheckpointDiff;

/// <summary><see cref="ComparisonText"/> is the host comparison of the tracked paths that could be compared, never Git's raw patch;
/// every tracked changed path is either in it or in <see cref="Omissions"/> with a fixed reason. <see cref="IsComplete"/> means no
/// tracked path was omitted. <see cref="Limitation"/> states what the comparison is not.</summary>
public sealed record GetGitCheckpointDiffResponse(
    string FingerprintSha256,
    string ComparisonText,
    bool IsComplete,
    int TrackedPathCount,
    int ComparedPathCount,
    string Limitation,
    IReadOnlyList<GetGitCheckpointDiffOmissionResponse> Omissions);
