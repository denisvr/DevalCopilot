using System.Text;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class GitHubRepositoryIdentityBindingTests
{
    private static readonly string Sha = new('a', 40);
    private static readonly DateTimeOffset TargetRecorded = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Observed = TargetRecorded.AddMinutes(1);
    private static readonly DateTimeOffset Recorded = TargetRecorded.AddMinutes(2);

    private static GitHubRepositoryIdentityBinding Record(
        Guid? id = null, string? sha = null, string? branch = null, string? node = null, string? owner = null, string? repository = null,
        DateTimeOffset? targetRecorded = null, DateTimeOffset? observed = null, DateTimeOffset? recorded = null, Guid? eventId = null,
        Guid? targetId = null) =>
        GitHubRepositoryIdentityBinding.Record(
            id ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), targetId ?? Guid.NewGuid(),
            sha ?? Sha, branch ?? "devalcopilot/workspace/abc/1", node ?? "R_kgDOExample01", owner ?? "octo-cat",
            repository ?? "hello.world", targetRecorded ?? TargetRecorded, observed ?? Observed, recorded ?? Recorded,
            eventId ?? Guid.NewGuid());

    [Fact]
    public void A_binding_keeps_exactly_the_facts_it_was_recorded_with()
    {
        var id = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        var binding = Record(id: id, eventId: eventId, targetId: targetId);

        Assert.Equal(id, binding.Id);
        Assert.Equal(eventId, binding.EventId);
        Assert.Equal(targetId, binding.ObservationTargetId);
        Assert.Equal(Sha, binding.CommitSha);
        Assert.Equal("R_kgDOExample01", binding.RepositoryNodeId);
        Assert.Equal("octo-cat", binding.Owner);
        Assert.Equal("hello.world", binding.Repository);
        Assert.Equal(Observed, binding.ObservedAtUtc);
        Assert.Equal(Recorded, binding.RecordedAtUtc);
    }

    [Fact]
    public void Every_identifier_is_required()
    {
        var empty = Guid.Empty;
        Assert.Throws<ArgumentException>(() => Record(id: empty));
        Assert.Throws<ArgumentException>(() => Record(eventId: empty));
        Assert.Throws<ArgumentException>(() => Record(targetId: empty));
        foreach (var position in Enumerable.Range(0, 4))
        {
            var ids = Enumerable.Range(0, 4).Select(index => index == position ? Guid.Empty : Guid.NewGuid()).ToArray();
            Assert.Throws<ArgumentException>(() => GitHubRepositoryIdentityBinding.Record(
                Guid.NewGuid(), ids[0], ids[1], ids[2], ids[3], Guid.NewGuid(), Sha, "b", "R_x", "octo-cat", "hello", TargetRecorded,
                Observed, Recorded, Guid.NewGuid()));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCD")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggg")]
    public void A_commit_sha_other_than_forty_lowercase_hex_characters_is_refused(string sha) =>
        Assert.Throws<ArgumentException>(() => Record(sha: sha));

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("R.dot")]
    [InlineData("R\\u0067")]
    public void A_node_identifier_outside_the_supported_subset_is_refused(string node) =>
        Assert.Throws<ArgumentException>(() => Record(node: node));

    [Fact]
    public void A_node_identifier_longer_than_the_bound_is_refused_and_the_bound_itself_is_admitted()
    {
        Assert.Equal(256, Record(node: new string('+', 256)).RepositoryNodeId.Length);
        Assert.Throws<ArgumentException>(() => Record(node: new string('+', 257)));
    }

    [Theory]
    [InlineData("-a", "hello")]
    [InlineData("octo-cat", ".hidden")]
    [InlineData("a b", "hello")]
    public void Names_outside_the_supported_subset_are_refused(string owner, string repository) =>
        Assert.Throws<ArgumentException>(() => Record(owner: owner, repository: repository));

    [Fact]
    public void A_branch_must_be_present_and_bounded()
    {
        Assert.Throws<ArgumentException>(() => Record(branch: " "));
        Assert.Throws<ArgumentException>(() => Record(branch: new string('b', GitHubObservationTarget.MaximumBranchNameLength + 1)));
    }

    [Fact]
    public void Every_time_must_be_utc()
    {
        var offset = new DateTimeOffset(2026, 10, 13, 15, 0, 0, TimeSpan.FromHours(1));
        Assert.Throws<ArgumentException>(() => Record(targetRecorded: offset.AddHours(-1).ToOffset(TimeSpan.FromHours(1)), observed: Observed));
        Assert.Throws<ArgumentException>(() => Record(observed: Observed.ToOffset(TimeSpan.FromHours(1))));
        Assert.Throws<ArgumentException>(() => Record(recorded: Recorded.ToOffset(TimeSpan.FromHours(1))));
    }

    [Fact]
    public void The_target_must_be_recorded_before_it_is_observed_and_observed_before_it_is_bound_with_equality_allowed()
    {
        Assert.Throws<ArgumentException>(() => Record(observed: TargetRecorded.AddTicks(-1)));
        Assert.Throws<ArgumentException>(() => Record(recorded: Observed.AddTicks(-1)));
        Assert.Equal(TargetRecorded, Record(observed: TargetRecorded, recorded: TargetRecorded).ObservedAtUtc);
    }

    [Fact]
    public void The_canonical_payload_is_exact_ordered_and_within_the_four_kibibyte_bound_at_the_longest_supported_values()
    {
        var binding = Record(
            sha: new string('f', 40),
            branch: new string('b', GitHubObservationTarget.MaximumBranchNameLength),
            node: new string('+', 256),
            owner: new string('o', 39),
            repository: new string('r', 100),
            observed: new DateTimeOffset(2026, 10, 13, 12, 1, 2, TimeSpan.Zero).AddTicks(1234567));

        var payload = GitHubRepositoryIdentityBindingEventPayload.Build(binding);

        Assert.StartsWith("{\"version\":1,\"bindingId\":\"", payload, StringComparison.Ordinal);
        Assert.Contains("\"host\":\"github.com\"", payload, StringComparison.Ordinal);
        Assert.EndsWith("\"observedAtUtc\":\"2026-10-13T12:01:02.1234567+00:00\"}", payload, StringComparison.Ordinal);
        Assert.True(Encoding.UTF8.GetByteCount(payload) <= GitHubRepositoryIdentityBindingEventPayload.MaximumLength);
        Assert.Equal(4096, GitHubRepositoryIdentityBindingEventPayload.MaximumLength);
        Assert.Equal(payload, GitHubRepositoryIdentityBindingEventPayload.Build(binding));
    }

    [Fact]
    public void The_payload_names_only_the_recorded_identity_and_no_authority_or_credential()
    {
        var payload = GitHubRepositoryIdentityBindingEventPayload.Build(Record());

        using var document = System.Text.Json.JsonDocument.Parse(payload);
        Assert.Equal(
            ["bindingId", "branchName", "commitSha", "host", "observedAtUtc", "operationId", "owner", "repository", "repositoryNodeId", "targetId", "version"],
            document.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    }
}
