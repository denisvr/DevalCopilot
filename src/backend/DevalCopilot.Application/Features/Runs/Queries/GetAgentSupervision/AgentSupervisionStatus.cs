namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentSupervision;

/// <summary>
/// The closed status vocabulary of the current Agent supervision read (ADR-0056). It describes the host's own observation of one
/// recorded Agent attempt, never provider progress, a live process or execution authority. The names are API schema.
/// </summary>
public enum AgentSupervisionStatus
{
    /// <summary>The run has no Agent attempt.</summary>
    NoAgentAttempt = 0,

    /// <summary>The selected Agent attempt has no observation: it was never observed or predates observations. Never a heartbeat.</summary>
    NotObserved = 1,

    /// <summary>The attempt is Running and its open observation is fresh and within its phase deadline.</summary>
    Observed = 2,

    /// <summary>The attempt is Running and the observation has missed a deadline, ended or was interrupted.</summary>
    NeedsAttention = 3,

    /// <summary>The attempt is terminal. Its real status and outcome govern; any recorded attention history stays visible.</summary>
    Ended = 4,

    /// <summary>The facts are ambiguous, incoherent or not coherent with the clock; nothing is disclosed as fresh.</summary>
    Unavailable = 5,
}
