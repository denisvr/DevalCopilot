namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>The outcome of selecting complete tracked-file hunks under a byte budget. <see cref="Text"/> holds whole
/// file headers and whole hunks only, in original order; it is a subset of the captured diff, never an applyable
/// patch. <see cref="Items"/> lists every file that is not fully present.</summary>
internal sealed record TrackedDiffSelection(
    string Text,
    IReadOnlyList<TrackedDiffItem> Items,
    int TotalFiles,
    int IncludedFiles,
    int PartialFiles,
    int OmittedFiles,
    int TotalHunks,
    int IncludedHunks);
