namespace DevalCopilot.Application.Tests.Features.Projects.ProjectVerificationInspection;

/// <summary>Every result shape the Domain transitions can record for one verification execution.</summary>
public enum ExecutionShape
{
    Running,
    RunningDispatched,
    Passed,
    FailedExited,
    FailedOutcome,
    TimedOut,
    Cancelled,
    Interrupted,
    InterruptedDispatched,
    SourceChangedBeforeDispatch,
    SourceChangedExited,
    SourceChangedTimedOut,
    DispatchRefused,
}
