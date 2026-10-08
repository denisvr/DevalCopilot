namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>The exact tree, commit, metadata and index identities derived from admitted bytes. The prepared index is an owned
/// artifact; nothing here authorizes a ref change by itself.</summary>
public sealed record LocalCommitPreparedFacts(
    string TreeSha,
    string CommitSha,
    string AuthorName,
    string AuthorEmail,
    long CommitTimeUnixSeconds,
    string IndexPreimageSha256,
    string PreparedIndexSha256,
    string PreparedIndexRelativePath,
    int ChangedPathCount,
    long TotalBytes);
