namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>Accounting for one tracked file that is not fully present in the selected diff text. <see cref="Selection"/>
/// is <c>partial</c> or <c>omitted</c>; <see cref="Reason"/> is a fixed value and never carries repository text.</summary>
internal sealed record TrackedDiffItem(
    string? Path, TrackedDiffFileKind Kind, int TotalHunks, int IncludedHunks, string Selection, string Reason);
