namespace DevalCopilot.Application.Features.Runs.Policies.AgentSupervisions;

/// <summary>
/// The identifier generated once per host launch (ADR-0056). A correlation fact stored on every observation the host starts, so a later
/// launch never adopts or renews it and never accepts its freshness. It is not a credential, a lease or a dispatch ticket.
/// </summary>
public sealed record AgentSupervisionHostInstance(Guid Id)
{
    public static AgentSupervisionHostInstance CreateForThisLaunch() => new(Guid.NewGuid());
}
