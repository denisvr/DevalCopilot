namespace DevalCopilot.Domain.Features.Runs;

public enum AttemptStatus
{
    Running = 0,
    Completed = 1,
    Failed = 2,

    /// <summary>An application restart found this attempt still <see cref="Running"/> with no
    /// owning process to observe. Terminal: a later intentional re-run creates a new attempt
    /// rather than resuming this one.</summary>
    Interrupted = 3,
}
