using System.Text;
using System.Text.Json;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class GitHubBranchPublicationApprovalTests
{
    private static readonly string Sha = new('a', 40);
    private static readonly DateTimeOffset TargetRecorded = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Observed = TargetRecorded.AddMinutes(1);
    private static readonly DateTimeOffset BindingRecorded = TargetRecorded.AddMinutes(2);
    private static readonly DateTimeOffset Approved = TargetRecorded.AddMinutes(3);

    private static GitHubBranchPublicationApproval Record(
        Guid? id = null, Guid? eventId = null, Guid? runId = null, Guid? projectId = null, Guid? operationId = null,
        Guid? workspaceId = null, string? sha = null, string? branch = null, Guid? targetId = null, Guid? targetEventId = null,
        string? targetOwner = null, string? targetRepository = null, DateTimeOffset? targetRecorded = null, Guid? bindingId = null,
        Guid? bindingEventId = null, string? node = null, string? bindingOwner = null, string? bindingRepository = null,
        DateTimeOffset? observed = null, DateTimeOffset? bindingRecorded = null, DateTimeOffset? approved = null) =>
        GitHubBranchPublicationApproval.Record(
            id ?? Guid.NewGuid(), eventId ?? Guid.NewGuid(), runId ?? Guid.NewGuid(), projectId ?? Guid.NewGuid(),
            operationId ?? Guid.NewGuid(), workspaceId ?? Guid.NewGuid(), sha ?? Sha, branch ?? "devalcopilot/workspace/abc/1",
            targetId ?? Guid.NewGuid(), targetEventId ?? Guid.NewGuid(), targetOwner ?? "octo-cat", targetRepository ?? "hello.world",
            targetRecorded ?? TargetRecorded, bindingId ?? Guid.NewGuid(), bindingEventId ?? Guid.NewGuid(), node ?? "R_kgDOExample01",
            bindingOwner ?? "Octo-Cat", bindingRepository ?? "Hello.World", observed ?? Observed, bindingRecorded ?? BindingRecorded,
            approved ?? Approved);

    [Fact]
    public void An_approval_keeps_exactly_the_facts_it_was_recorded_with_and_the_fixed_contract()
    {
        var ids = Enumerable.Range(0, 9).Select(_ => Guid.NewGuid()).ToArray();

        var approval = Record(
            id: ids[0], eventId: ids[1], runId: ids[2], projectId: ids[3], operationId: ids[4], workspaceId: ids[5], targetId: ids[6],
            targetEventId: ids[7], bindingId: ids[8], bindingEventId: Guid.Parse("00000000-0000-0000-0000-0000000000aa"));

        Assert.Equal(ids[0], approval.Id);
        Assert.Equal(ids[1], approval.EventId);
        Assert.Equal(ids[2], approval.RunId);
        Assert.Equal(ids[3], approval.ProjectId);
        Assert.Equal(ids[4], approval.LocalCommitOperationId);
        Assert.Equal(ids[5], approval.GitWorkspaceId);
        Assert.Equal(ids[6], approval.TargetId);
        Assert.Equal(ids[7], approval.TargetEventId);
        Assert.Equal(ids[8], approval.BindingId);
        Assert.Equal(Guid.Parse("00000000-0000-0000-0000-0000000000aa"), approval.BindingEventId);
        Assert.Equal(Sha, approval.CommitSha);
        Assert.Equal("devalcopilot/workspace/abc/1", approval.BranchName);
        Assert.Equal("octo-cat", approval.TargetOwner);
        Assert.Equal("hello.world", approval.TargetRepository);
        Assert.Equal("Octo-Cat", approval.BindingOwner);
        Assert.Equal("Hello.World", approval.BindingRepository);
        Assert.Equal("R_kgDOExample01", approval.RepositoryNodeId);
        Assert.Equal(TargetRecorded, approval.TargetRecordedAtUtc);
        Assert.Equal(Observed, approval.BindingObservedAtUtc);
        Assert.Equal(BindingRecorded, approval.BindingRecordedAtUtc);
        Assert.Equal(Approved, approval.ApprovedAtUtc);
        Assert.Equal("github.com", GitHubBranchPublicationApproval.Host);
        Assert.Equal("PushExactCommit", GitHubBranchPublicationApproval.Action);
        Assert.Equal(1, GitHubBranchPublicationApproval.PolicyVersion);
    }

    [Fact]
    public void Every_identifier_is_required()
    {
        var empty = Guid.Empty;
        Assert.Throws<ArgumentException>(() => Record(id: empty));
        Assert.Throws<ArgumentException>(() => Record(eventId: empty));
        Assert.Throws<ArgumentException>(() => Record(runId: empty));
        Assert.Throws<ArgumentException>(() => Record(projectId: empty));
        Assert.Throws<ArgumentException>(() => Record(operationId: empty));
        Assert.Throws<ArgumentException>(() => Record(workspaceId: empty));
        Assert.Throws<ArgumentException>(() => Record(targetId: empty));
        Assert.Throws<ArgumentException>(() => Record(targetEventId: empty));
        Assert.Throws<ArgumentException>(() => Record(bindingId: empty));
        Assert.Throws<ArgumentException>(() => Record(bindingEventId: empty));
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
    public void A_node_identifier_outside_the_supported_subset_is_refused(string node) =>
        Assert.Throws<ArgumentException>(() => Record(node: node));

    [Theory]
    [InlineData("-a")]
    [InlineData("a b")]
    [InlineData("")]
    public void An_owner_outside_the_supported_subset_is_refused_in_either_snapshot(string owner)
    {
        Assert.Throws<ArgumentException>(() => Record(targetOwner: owner, bindingOwner: owner));
        Assert.Throws<ArgumentException>(() => Record(targetOwner: owner, bindingOwner: "octo-cat"));
        Assert.Throws<ArgumentException>(() => Record(targetOwner: "octo-cat", bindingOwner: owner));
    }

    [Theory]
    [InlineData(".hidden")]
    [InlineData("a b")]
    [InlineData("")]
    public void A_repository_outside_the_supported_subset_is_refused_in_either_snapshot(string repository)
    {
        Assert.Throws<ArgumentException>(() => Record(targetRepository: repository, bindingRepository: repository));
        Assert.Throws<ArgumentException>(() => Record(targetRepository: repository, bindingRepository: "hello.world"));
        Assert.Throws<ArgumentException>(() => Record(targetRepository: "hello.world", bindingRepository: repository));
    }

    [Fact]
    public void The_binding_names_may_differ_from_the_target_names_only_in_ascii_case()
    {
        Assert.Equal("OCTO-CAT", Record(targetOwner: "octo-cat", bindingOwner: "OCTO-CAT").BindingOwner);
        Assert.Throws<ArgumentException>(() => Record(targetOwner: "octo-cat", bindingOwner: "octo-dog"));
        Assert.Throws<ArgumentException>(() => Record(targetRepository: "hello", bindingRepository: "world"));
    }

    [Fact]
    public void A_branch_must_be_present_bounded_and_printable_ascii()
    {
        Assert.Throws<ArgumentException>(() => Record(branch: " "));
        Assert.Throws<ArgumentException>(() => Record(branch: "has space"));
        Assert.Throws<ArgumentException>(() => Record(branch: "caf" + char.ConvertFromUtf32(0xE9)));
        Assert.Throws<ArgumentException>(() => Record(branch: new string('b', GitHubObservationTarget.MaximumBranchNameLength + 1)));
        Assert.Equal(512, Record(branch: new string('b', GitHubObservationTarget.MaximumBranchNameLength)).BranchName.Length);
    }

    [Fact]
    public void Every_time_must_be_utc()
    {
        var plus = TimeSpan.FromHours(1);
        Assert.Throws<ArgumentException>(() => Record(targetRecorded: TargetRecorded.ToOffset(plus)));
        Assert.Throws<ArgumentException>(() => Record(observed: Observed.ToOffset(plus)));
        Assert.Throws<ArgumentException>(() => Record(bindingRecorded: BindingRecorded.ToOffset(plus)));
        Assert.Throws<ArgumentException>(() => Record(approved: Approved.ToOffset(plus)));
    }

    [Fact]
    public void Times_are_ordered_target_then_observation_then_binding_then_approval_and_equality_is_valid_without_clamping()
    {
        Assert.Throws<ArgumentException>(() => Record(observed: TargetRecorded.AddTicks(-1)));
        Assert.Throws<ArgumentException>(() => Record(bindingRecorded: Observed.AddTicks(-1)));
        Assert.Throws<ArgumentException>(() => Record(approved: BindingRecorded.AddTicks(-1)));

        var equal = Record(targetRecorded: BindingRecorded, observed: BindingRecorded, bindingRecorded: BindingRecorded, approved: BindingRecorded);

        Assert.Equal(BindingRecorded, equal.ApprovedAtUtc);
        Assert.Equal(BindingRecorded, equal.BindingRecordedAtUtc);
    }

    [Fact]
    public void The_canonical_payload_is_exact_ordered_complete_and_within_the_four_kibibyte_bound_at_the_longest_supported_values()
    {
        var approval = Record(
            sha: new string('f', 40),
            branch: new string('b', GitHubObservationTarget.MaximumBranchNameLength),
            node: new string('+', 256),
            targetOwner: new string('o', 39),
            targetRepository: new string('r', 100),
            bindingOwner: new string('O', 39),
            bindingRepository: new string('R', 100),
            observed: new DateTimeOffset(2026, 10, 14, 12, 1, 2, TimeSpan.Zero).AddTicks(1234567),
            bindingRecorded: new DateTimeOffset(2026, 10, 14, 12, 1, 3, TimeSpan.Zero).AddTicks(1234567),
            approved: new DateTimeOffset(2026, 10, 14, 12, 1, 4, TimeSpan.Zero).AddTicks(1234567));

        var payload = GitHubBranchPublicationApprovalEventPayload.Build(approval);

        Assert.StartsWith("{\"version\":1,\"approvalId\":\"" + approval.Id + "\"", payload, StringComparison.Ordinal);
        Assert.EndsWith("\"approvedAtUtc\":\"2026-10-14T12:01:04.1234567+00:00\"}", payload, StringComparison.Ordinal);
        Assert.True(Encoding.UTF8.GetByteCount(payload) <= GitHubBranchPublicationApprovalEventPayload.MaximumLength);
        Assert.Equal(4096, GitHubBranchPublicationApprovalEventPayload.MaximumLength);
        Assert.Equal(payload, GitHubBranchPublicationApprovalEventPayload.Build(approval));
    }

    [Fact]
    public void The_payload_is_the_complete_snapshot_and_names_no_credential_url_refspec_or_message()
    {
        var approval = Record();

        using var document = JsonDocument.Parse(GitHubBranchPublicationApprovalEventPayload.Build(approval));
        var root = document.RootElement;

        Assert.Equal(
            [
                "action", "approvalId", "approvedAtUtc", "binding", "branchName", "commitSha", "host", "operationId", "policyVersion",
                "projectId", "runId", "target", "version", "workspaceId",
            ],
            root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["eventId", "id", "observedAtUtc", "owner", "recordedAtUtc", "repository", "repositoryNodeId"],
            root.GetProperty("binding").EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["eventId", "id", "owner", "recordedAtUtc", "repository"],
            root.GetProperty("target").EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal("github.com", root.GetProperty("host").GetString());
        Assert.Equal("PushExactCommit", root.GetProperty("action").GetString());
        Assert.Equal(1, root.GetProperty("policyVersion").GetInt32());
        Assert.Equal("octo-cat", root.GetProperty("target").GetProperty("owner").GetString());
        Assert.Equal("Octo-Cat", root.GetProperty("binding").GetProperty("owner").GetString());
        Assert.Equal(approval.TargetEventId, root.GetProperty("target").GetProperty("eventId").GetGuid());
        Assert.Equal(approval.BindingEventId, root.GetProperty("binding").GetProperty("eventId").GetGuid());
    }

    [Fact]
    public void Any_changed_fact_changes_the_payload()
    {
        var ids = Enumerable.Range(1, 9).Select(index => Guid.Parse($"{index}{index}{index}{index}{index}{index}{index}{index}-0000-0000-0000-000000000000")).ToArray();

        GitHubBranchPublicationApproval Same(string? node = null, string? sha = null, DateTimeOffset? approved = null) => Record(
            id: ids[0], eventId: ids[1], runId: ids[2], projectId: ids[3], operationId: ids[4], workspaceId: ids[5], targetId: ids[6],
            targetEventId: ids[7], bindingId: ids[8], bindingEventId: ids[0], node: node, sha: sha, approved: approved);

        var baseline = GitHubBranchPublicationApprovalEventPayload.Build(Same());

        Assert.Equal(baseline, GitHubBranchPublicationApprovalEventPayload.Build(Same()));
        Assert.NotEqual(baseline, GitHubBranchPublicationApprovalEventPayload.Build(Same(node: "R_other")));
        Assert.NotEqual(baseline, GitHubBranchPublicationApprovalEventPayload.Build(Same(sha: new string('b', 40))));
        Assert.NotEqual(baseline, GitHubBranchPublicationApprovalEventPayload.Build(Same(approved: Approved.AddTicks(1))));
    }
}
