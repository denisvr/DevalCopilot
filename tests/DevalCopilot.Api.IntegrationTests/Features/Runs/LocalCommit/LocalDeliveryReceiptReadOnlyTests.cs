using System.Net;
using DevalCopilot.Application.Features.Runs.Ports;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>ADR-0032: reading a receipt is pure persisted-fact reading. It writes no row, appends no event, takes no lease and
/// reaches neither the local-commit Git adapter nor the filesystem state of the repository.</summary>
public sealed class LocalDeliveryReceiptReadOnlyTests : LocalDeliveryReceiptTestBase
{
    [Fact]
    public async Task Reading_every_state_of_the_receipt_reaches_no_git_adapter_and_changes_no_row_event_ref_or_file()
    {
        // A completed delivery needs the real executor, so a supervising host produces it (and a later, undelivered run in the same
        // workspace); the receipt is then read by a host that has no supervisor and whose Git adapters are tripwired.
        LocalCommitLineageIds delivered;
        LocalCommitLineageIds undelivered;
        using (var delivering = StartHost(runSupervisor: true))
        {
            (delivered, _) = await DeliverAsync(delivering, recipes: 2, completeSet: true);
            undelivered = await LocalCommitLineage.SeedAsync(delivering, Scene, continueFrom: delivered);
        }

        var tripwire = new ExternalCallTripwire();
        using var reader = StartHost(
            runSupervisor: false,
            decorate: repository => tripwire.Wrap<ILocalCommitRepository>(repository),
            decoratePreparer: preparer => tripwire.Wrap<ILocalCommitPreparer>(preparer));
        tripwire.Arm();

        var databaseBefore = await DatabaseFingerprintAsync();
        var mainBefore = Scene.MainRepositoryFingerprint();
        var workspaceBefore = Scene.RunWorkspaceGit("status", "--porcelain=v1", "-z");
        var tipBefore = Scene.RunMainGit("rev-parse", "refs/heads/" + Scene.BranchName).Trim();
        var storageBefore = Scene.StorageLeaves();
        var eventsBefore = await EventTypeRowsAsync(delivered.RunId);

        Assert.Equal("NotRecorded", (await ReceiptAsync(reader, undelivered.RunId)).State);
        for (var read = 0; read < 3; read++)
        {
            Assert.Equal("Available", (await ReceiptAsync(reader, delivered.RunId)).State);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await ReceiptAsync(reader, Guid.NewGuid())).Status);

        Assert.Empty(tripwire.Calls);
        Assert.Equal(databaseBefore, await DatabaseFingerprintAsync());
        Assert.Equal(eventsBefore, await EventTypeRowsAsync(delivered.RunId));
        Assert.Equal(mainBefore, Scene.MainRepositoryFingerprint());
        Assert.Equal(workspaceBefore, Scene.RunWorkspaceGit("status", "--porcelain=v1", "-z"));
        Assert.Equal(tipBefore, Scene.RunMainGit("rev-parse", "refs/heads/" + Scene.BranchName).Trim());
        Assert.Equal(storageBefore, Scene.StorageLeaves());
    }
}
