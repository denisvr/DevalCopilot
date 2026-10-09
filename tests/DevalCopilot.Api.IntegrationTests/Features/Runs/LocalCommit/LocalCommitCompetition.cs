using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>Two identical requests of one operation whose real preparations run concurrently and park, after each result is captured,
/// until the test releases that result. Disposing releases every gate (the failure-safe path) and touches no host, lock, checkout or
/// directory; the fixture stops the hosts.</summary>
internal sealed class LocalCommitCompetition(
    LocalCommitHost host,
    ScriptedLocalCommitRepository repository,
    ScriptedLocalCommitPreparer preparer,
    PreparationGate gate,
    LocalCommitLineageIds ids,
    Guid operationId,
    IReadOnlyList<Task<HttpResponseMessage>> requests) : IDisposable
{
    public static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    private readonly List<Task<HttpResponseMessage>> _pending = [.. requests];

    public LocalCommitHost Host => host;

    public ScriptedLocalCommitRepository Repository => repository;

    public ScriptedLocalCommitPreparer Preparer => preparer;

    public PreparationGate Gate => gate;

    public LocalCommitLineageIds Ids => ids;

    public Guid OperationId => operationId;

    /// <summary>The response of whichever request answers next. A request that is still parked cannot answer, so after releasing one
    /// completion this is that completion's request.</summary>
    public async Task<HttpResponseMessage> NextResponseAsync()
    {
        var next = await Task.WhenAny(_pending).WaitAsync(Bound);
        _pending.Remove(next);
        return await next;
    }

    public void AssertStillParked() =>
        Assert.All(_pending, request => Assert.False(request.IsCompleted, "a request answered before its completion was released"));

    public void Dispose() => gate.ReleaseAll();
}
