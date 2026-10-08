namespace DevalCopilot.Application.Features.Runs.Ports;

public enum LocalCommitObjectState
{
    Absent,

    /// <summary>The object exists with exactly the recorded tree, parent, message with trailer, author and committer, and no
    /// signature header.</summary>
    ExactMatch,
    Mismatch,
}
