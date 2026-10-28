namespace DevalCopilot.Application.Features.Runs.Policies.RecordedLocalCommit;

/// <summary><see cref="NotFound"/>: an unknown run, or one whose registered project is absent. <see cref="NotRecorded"/>: the registered
/// run has no operation associated with it by its run key. <see cref="Unavailable"/>: an associated operation exists (or more than one)
/// but its selected facts cannot be admitted or its ownership cannot be proved. <see cref="Recorded"/>: every selected fact is proved.</summary>
internal enum RecordedLocalCommitReadingKind
{
    NotFound,
    NotRecorded,
    Unavailable,
    Recorded,
}
