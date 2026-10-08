namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>One path of the approved checkpoint exactly as Git's porcelain reported it; the host re-derives its meaning.</summary>
public sealed record LocalCommitChangedPath(string Path, string IndexStatus, string WorkTreeStatus);
