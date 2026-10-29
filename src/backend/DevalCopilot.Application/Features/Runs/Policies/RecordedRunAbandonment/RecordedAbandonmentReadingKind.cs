namespace DevalCopilot.Application.Features.Runs.Policies.RecordedRunAbandonment;

/// <summary><see cref="NotFound"/>: an unknown run, or one whose registered project is absent. <see cref="NotRecorded"/>: a recognized
/// non-Abandoned run with no abandonment column and no exactly associated event. <see cref="Unavailable"/>: recorded markers or events
/// exist (or are damaged or doubled) but cannot pass the selected proof. <see cref="Recorded"/>: every selected fact is proved.</summary>
internal enum RecordedAbandonmentReadingKind
{
    NotFound,
    NotRecorded,
    Unavailable,
    Recorded,
}
