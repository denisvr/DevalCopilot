namespace DevalCopilot.Application.Tests.Features.Runs.RunAbandonmentInspection;

/// <summary>One run closed by the production abandonment command and the stored facts of its one supporting event, as the database holds them.</summary>
internal sealed record AbandonedRun(
    AbandonmentScene Scene, string Reason, DateTimeOffset AbandonedAt, Guid EventId, long EventSequence)
{
    public Guid RunId => Scene.RunId;

    public Guid ProjectId => Scene.ProjectId;
}
