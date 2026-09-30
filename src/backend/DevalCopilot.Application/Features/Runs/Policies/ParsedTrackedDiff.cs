namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>The parse of a captured tracked diff. When <see cref="Recognized"/> is false the text does not begin
/// with a git file header, so no file boundary can be trusted and <see cref="Files"/> is empty.</summary>
internal sealed record ParsedTrackedDiff(bool Recognized, IReadOnlyList<TrackedDiffFile> Files, bool ContainsBinaryPatch);
