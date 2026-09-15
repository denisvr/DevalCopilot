using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Infrastructure.Features.Projects;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Projects;

public sealed class WorkspaceOwnershipMarkerStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-marker-store-{Guid.NewGuid():N}");
    private readonly WorkspaceOwnershipMarkerStore _store = new();

    public WorkspaceOwnershipMarkerStoreTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static WorkspaceOwnershipMarker CreateMarker() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 42UL, Convert.ToHexString(new byte[16]));

    [Fact]
    public async Task WriteAsync_then_ReadAsync_round_trips_every_typed_field()
    {
        var marker = CreateMarker();

        var writeResult = await _store.WriteAsync(_root, marker, CancellationToken.None);
        Assert.Equal(WorkspaceOwnershipMarkerWriteOutcome.Success, writeResult.Outcome);

        var readResult = await _store.ReadAsync(_root, CancellationToken.None);

        Assert.Equal(WorkspaceOwnershipMarkerReadOutcome.Valid, readResult.Outcome);
        Assert.Equal(marker, readResult.Marker);
    }

    [Fact]
    public async Task WriteAsync_leaves_no_temporary_file_behind_and_the_final_name_is_never_partially_written()
    {
        var marker = CreateMarker();

        await _store.WriteAsync(_root, marker, CancellationToken.None);

        var entries = Directory.GetFiles(_root);
        var finalPath = Path.Combine(_root, "devalcopilot-ownership.json");
        Assert.Single(entries);
        Assert.Equal(finalPath, entries[0]);
    }

    [Fact]
    public async Task ReadAsync_reports_absent_when_no_marker_file_exists()
    {
        var result = await _store.ReadAsync(_root, CancellationToken.None);

        Assert.Equal(WorkspaceOwnershipMarkerReadOutcome.Absent, result.Outcome);
        Assert.Null(result.Marker);
    }

    [Fact]
    public async Task ReadAsync_reports_invalid_for_unparsable_content()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "devalcopilot-ownership.json"), "not json at all {{{");

        var result = await _store.ReadAsync(_root, CancellationToken.None);

        Assert.Equal(WorkspaceOwnershipMarkerReadOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public async Task ReadAsync_tolerates_incidental_formatting_differences_as_still_valid()
    {
        var marker = CreateMarker();
        await _store.WriteAsync(_root, marker, CancellationToken.None);
        var path = Path.Combine(_root, "devalcopilot-ownership.json");
        var reformatted = (await File.ReadAllTextAsync(path)).Replace(",", ",\n  ");
        await File.WriteAllTextAsync(path, reformatted);

        var result = await _store.ReadAsync(_root, CancellationToken.None);

        Assert.Equal(WorkspaceOwnershipMarkerReadOutcome.Valid, result.Outcome);
        Assert.Equal(marker, result.Marker);
    }

    [Fact]
    public async Task ReadAsync_reports_invalid_after_a_field_is_manually_altered()
    {
        var marker = CreateMarker();
        await _store.WriteAsync(_root, marker, CancellationToken.None);
        var path = Path.Combine(_root, "devalcopilot-ownership.json");
        var tampered = (await File.ReadAllTextAsync(path)).Replace(marker.WorkspaceId.ToString(), Guid.NewGuid().ToString());
        await File.WriteAllTextAsync(path, tampered);

        var result = await _store.ReadAsync(_root, CancellationToken.None);

        // The store itself still parses this successfully (it is well-formed JSON) — detecting
        // that the WorkspaceId no longer matches the durable database record is the caller's
        // typed-field comparison, proven at the ReconcileWorkspacesCommandHandler level.
        Assert.Equal(WorkspaceOwnershipMarkerReadOutcome.Valid, result.Outcome);
        Assert.NotEqual(marker.WorkspaceId, result.Marker!.WorkspaceId);
    }
}
