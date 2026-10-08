using DevalCopilot.Application.Features.Runs.Ports;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>A transparent decorator over the REAL preparer whose only purpose is to run a test action in the exact window between
/// the pre-admission reads/preparation and the short locked admission transaction, where a competing writer can commit. The host
/// resolves the preparer lazily, so the test creates this object first and the host attaches the real preparer to it.</summary>
internal sealed class ScriptedLocalCommitPreparer : ILocalCommitPreparer
{
    private ILocalCommitPreparer? _inner;

    public Func<Task>? AfterPrepare { get; set; }

    public ScriptedLocalCommitPreparer Attach(ILocalCommitPreparer inner)
    {
        _inner = inner;
        return this;
    }

    public async Task<LocalCommitPreparationResult> PrepareAsync(
        LocalCommitPreparationRequest request, CancellationToken cancellationToken)
    {
        var result = await _inner!.PrepareAsync(request, cancellationToken);
        if (AfterPrepare is not null)
        {
            await AfterPrepare();
        }

        return result;
    }
}
