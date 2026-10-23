namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

internal enum LocalExecutionAdmissionHistoryReadingKind
{
    RunNotFound,
    NotRecorded,
    NotQueued,
    Unavailable,
    Recorded,
}
