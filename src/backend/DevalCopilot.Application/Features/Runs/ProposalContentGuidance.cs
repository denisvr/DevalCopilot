using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// The fixed, host-authored guidance a Codex planning manifest carries beside the expected Proposal schema so a provider can
/// avoid the literals that <see cref="CollaborationMessageContentPolicy"/> refuses. The literal list is rendered from
/// <see cref="CollaborationMessageContentPolicy.BlockedMarkers"/>, the one source the filter also reads, and the rest of the
/// text never repeats a blocked literal. It carries no run data, no human text and no provider output, it relaxes nothing
/// (the schema, the parser and the filter are unchanged), and a response that still contains a literal is refused exactly as
/// before: the host never rewrites a provider answer.
/// </summary>
internal static class ProposalContentGuidance
{
    /// <summary>The manifest member that carries <see cref="Text"/>.</summary>
    public const string Member = "proposalContentGuidance";

    /// <summary>The rendered literal list, quoted and comma-separated in the policy's own order.</summary>
    public static readonly string BlockedLiteralList = string.Join(
        ", ", CollaborationMessageContentPolicy.BlockedMarkers.Select(marker => "\"" + marker + "\""));

    public static readonly string Text =
        "Content rules for the Proposal you return. The host rejects the whole response when the summary or any of the five "
        + "text fields contains one of these literals, in any letter case, even inside an otherwise benign sentence: "
        + BlockedLiteralList + ". "
        + "Re-read every field, verificationPlan and risks especially, before you answer, and rephrase any sentence that contains one. "
        + "Say the same idea in other words. For the surrounding setup of a program, write runtime context, target platform, host "
        + "setup, configuration or settings. For sign-in handling, write authentication details, sign-in handling or access-token "
        + "handling, without any actual value. For confidential values, write protected values or sensitive configuration. "
        + "For a location, write a repository-relative path with forward slashes or name its role (the project root, the test "
        + "folder), and never a drive letter followed by a colon and a backslash. "
        + "Do not spell a blocked literal in pieces, with separators, in another encoding or inside a quotation, and do not "
        + "describe these rules in the Proposal. Keep every field a plain, bounded string as the expected schema requires.";
}
