namespace DevalCopilot.Application.Features.Runs.Policies.RecordedLocalCommit;

/// <summary><see cref="None"/>: no reason was recorded. <see cref="Known"/>: the recorded reason is one of the operation-owned finite
/// codes. <see cref="Unrecognized"/>: a bounded reason was recorded that this version does not describe; its text is never returned.</summary>
public enum RecordedLocalCommitReasonKind
{
    None,
    Known,
    Unrecognized,
}
