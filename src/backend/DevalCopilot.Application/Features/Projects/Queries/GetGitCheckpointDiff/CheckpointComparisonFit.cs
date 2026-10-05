using System.Text;
using DevalCopilot.Application.Features.Projects.Policies;

namespace DevalCopilot.Application.Features.Projects.Queries.GetGitCheckpointDiff;

/// <summary>
/// The response policy of checkpoint inspection (ADR-0027), separate from the Agent manifest fitting: whole file blocks, in
/// ordinal path order, are admitted while the comparison text stays within <see cref="MaxComparisonBytes"/> UTF-8 bytes. A block
/// that does not fit is omitted whole with the fixed reason <see cref="ComparisonLimitReason"/> and is never cut into
/// apparently complete text; a later, smaller block may still fit. There is no sampling, paging or optimization.
/// </summary>
internal static class CheckpointComparisonFit
{
    internal const int MaxComparisonBytes = 512 * 1024;

    internal const string ComparisonLimitReason = "comparison_limit";

    internal const string Limitation =
        "Host comparison of attested tracked sources, not Git's minimal or filter-normalized patch: each file is one replacement " +
        "hunk with at most three unchanged context lines at each edge, so unchanged lines inside the replaced middle can appear " +
        "removed and added and line-ending-only differences remain visible. File modes and renames are not compared, untracked " +
        "files are listed separately, and every tracked path without text is listed with a fixed reason.";

    internal static GetGitCheckpointDiffQueryResult Fit(string fingerprintSha256, IReadOnlyList<AttestedTrackedComparison.Entry> entries)
    {
        var text = new StringBuilder();
        var bytes = 0;
        var omissions = new List<GetGitCheckpointDiffOmission>();
        foreach (var entry in entries)
        {
            if (entry.Reason is { } reason)
            {
                omissions.Add(new GetGitCheckpointDiffOmission(entry.Path, reason));
                continue;
            }

            var blockBytes = Encoding.UTF8.GetByteCount(entry.Block!);
            if (bytes + blockBytes > MaxComparisonBytes)
            {
                omissions.Add(new GetGitCheckpointDiffOmission(entry.Path, ComparisonLimitReason));
                continue;
            }

            text.Append(entry.Block);
            bytes += blockBytes;
        }

        return new GetGitCheckpointDiffQueryResult(
            fingerprintSha256,
            text.ToString(),
            omissions.Count == 0,
            entries.Count,
            entries.Count - omissions.Count,
            Limitation,
            omissions);
    }
}
