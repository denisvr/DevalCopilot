using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class GitHubObservationTargetTests
{
    private static readonly string Sha = new('a', 40);
    private static readonly DateTimeOffset Recorded = new(2026, 10, 12, 20, 0, 0, TimeSpan.Zero);

    private static GitHubObservationTarget Record(
        Guid? id = null, string? sha = null, string? branch = null, string? owner = null, string? repository = null,
        DateTimeOffset? recorded = null, Guid? eventId = null) =>
        GitHubObservationTarget.Record(
            id ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            sha ?? Sha, branch ?? "devalcopilot/workspace/abc/1", owner ?? "octo-cat", repository ?? "hello.world",
            recorded ?? Recorded, eventId ?? Guid.NewGuid());

    [Fact]
    public void A_target_keeps_exactly_the_facts_it_was_recorded_with_and_the_fixed_host_and_purpose()
    {
        var id = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        var target = Record(id: id, eventId: eventId);

        Assert.Equal(id, target.Id);
        Assert.Equal(eventId, target.EventId);
        Assert.Equal(Sha, target.CommitSha);
        Assert.Equal("octo-cat", target.Owner);
        Assert.Equal("hello.world", target.Repository);
        Assert.Equal(Recorded, target.RecordedAtUtc);
        Assert.Equal("github.com", GitHubObservationTarget.Host);
        Assert.Equal("CiObservation", GitHubObservationTarget.Purpose);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCD")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggg")]
    [InlineData(" aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void A_commit_sha_other_than_forty_lowercase_hex_characters_is_refused(string sha) =>
        Assert.Throws<ArgumentException>(() => Record(sha: sha));

    [Theory]
    [InlineData("")]
    [InlineData("-a")]
    [InlineData("a b")]
    public void An_owner_outside_the_supported_subset_is_refused(string owner) =>
        Assert.Throws<ArgumentException>(() => Record(owner: owner));

    [Theory]
    [InlineData("")]
    [InlineData(".a")]
    [InlineData("a/b")]
    public void A_repository_outside_the_supported_subset_is_refused(string repository) =>
        Assert.Throws<ArgumentException>(() => Record(repository: repository));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_branch_is_refused(string branch) => Assert.Throws<ArgumentException>(() => Record(branch: branch));

    [Fact]
    public void A_branch_over_the_stored_bound_is_refused() =>
        Assert.Throws<ArgumentException>(() => Record(branch: new string('b', GitHubObservationTarget.MaximumBranchNameLength + 1)));

    [Fact]
    public void An_empty_identifier_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Record(id: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Record(eventId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => GitHubObservationTarget.Record(
            Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Sha, "b", "o", "r", Recorded, Guid.NewGuid()));
    }

    [Fact]
    public void A_recorded_time_with_an_offset_is_refused() =>
        Assert.Throws<ArgumentException>(() => Record(recorded: new DateTimeOffset(2026, 10, 12, 20, 0, 0, TimeSpan.FromHours(1))));

    [Fact]
    public void The_entity_exposes_no_mutating_member()
    {
        var mutators = typeof(GitHubObservationTarget).GetMethods(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .ToList();
        Assert.Empty(mutators);
        Assert.All(
            typeof(GitHubObservationTarget).GetProperties(),
            property => Assert.True(property.SetMethod is null || !property.SetMethod.IsPublic, property.Name));
    }

    [Fact]
    public void The_canonical_event_payload_is_the_closed_version_one_form_and_agrees_with_the_target()
    {
        var target = Record();

        var payload = GitHubObservationTargetEventPayload.Build(target);

        Assert.Equal(
            "{\"version\":1,\"targetId\":\"" + target.Id + "\",\"operationId\":\"" + target.LocalCommitOperationId
            + "\",\"commitSha\":\"" + Sha + "\",\"branchName\":\"devalcopilot/workspace/abc/1\",\"host\":\"github.com\","
            + "\"owner\":\"octo-cat\",\"repository\":\"hello.world\"}",
            payload);
        Assert.True(payload.Length <= GitHubObservationTargetEventPayload.MaximumLength);
    }
}
