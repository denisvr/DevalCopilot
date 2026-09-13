namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// How a Process attempt's child process ended, as a durable Domain fact. Deliberately a
/// separate type from the Application process-execution port's own outcome enum: Domain must
/// not reference Application, and this is the value actually persisted and reasoned about by
/// attempt-completion rules, independent of whatever shape the execution port happens to use.
/// </summary>
public enum ProcessOutcome
{
    Exited = 0,
    TimedOut = 1,
    Cancelled = 2,
}
