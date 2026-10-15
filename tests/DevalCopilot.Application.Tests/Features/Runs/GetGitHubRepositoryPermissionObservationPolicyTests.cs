using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryPermissionObservation;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The pure admission rules of the repository permission observation (ADR-0049): the six case-sensitive literals of GitHub's
/// <c>RepositoryPermission</c> enum, the explicit absence, and the whole-probe validation the handler applies before any comparison. The
/// exhaustive permutations live here, at the cheapest boundary; the endpoint tests prove representative wiring only.
/// </summary>
public sealed class GetGitHubRepositoryPermissionObservationPolicyTests
{
    private static readonly GitHubObservationTargetView Target = new(
        Guid.NewGuid(), Guid.NewGuid(), "github.com", "octo-cat", "hello.world", new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero));

    private static GitHubRepositoryPermissionProbe Probe(
        string nodeId = "R_kgDOExample01", string owner = "octo-cat", string repository = "hello.world", string? permission = "WRITE") =>
        new(nodeId, owner, repository, permission);

    [Theory]
    [InlineData("READ", true)]
    [InlineData("TRIAGE", true)]
    [InlineData("TRIAGE_PLUS", true)]
    [InlineData("WRITE", true)]
    [InlineData("MAINTAIN", true)]
    [InlineData("ADMIN", true)]
    [InlineData("read", false)]
    [InlineData("Read", false)]
    [InlineData("write", false)]
    [InlineData("triage_plus", false)]
    [InlineData("TRIAGE PLUS", false)]
    [InlineData("TRIAGE-PLUS", false)]
    [InlineData("TRIAGEPLUS", false)]
    [InlineData("READ ", false)]
    [InlineData(" READ", false)]
    [InlineData("READ\n", false)]
    [InlineData("TRIAGE_PLUS ", false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("OWNER", false)]
    [InlineData("NONE", false)]
    [InlineData("PUSH", false)]
    [InlineData("PULL", false)]
    [InlineData("CAN_PUSH", false)]
    [InlineData("ADMINISTRATOR", false)]
    [InlineData("MAINTAINER", false)]
    [InlineData("WRITER", false)]
    [InlineData("READ,WRITE", false)]
    public void The_viewer_permission_policy_admits_exactly_the_six_literals_and_nothing_else(string value, bool admitted)
    {
        Assert.Equal(admitted, GitHubRepositoryPermissionPolicy.IsViewerPermission(value));
    }

    [Fact]
    public void An_absent_permission_is_not_a_viewer_permission_literal()
    {
        Assert.False(GitHubRepositoryPermissionPolicy.IsViewerPermission(null));
    }

    [Fact]
    public void The_policy_admits_no_unlisted_code_point_permutation_of_a_literal()
    {
        foreach (var literal in new[] { "READ", "TRIAGE", "TRIAGE_PLUS", "WRITE", "MAINTAIN", "ADMIN" })
        {
            Assert.True(GitHubRepositoryPermissionPolicy.IsViewerPermission(literal));
            Assert.False(GitHubRepositoryPermissionPolicy.IsViewerPermission(literal + char.ConvertFromUtf32(0x200B)));
            Assert.False(GitHubRepositoryPermissionPolicy.IsViewerPermission(literal + "\0"));
            Assert.False(GitHubRepositoryPermissionPolicy.IsViewerPermission(literal.ToLowerInvariant()));
            Assert.False(GitHubRepositoryPermissionPolicy.IsViewerPermission("\t" + literal));
        }
    }

    [Theory]
    [InlineData("READ")]
    [InlineData("TRIAGE")]
    [InlineData("TRIAGE_PLUS")]
    [InlineData("WRITE")]
    [InlineData("MAINTAIN")]
    [InlineData("ADMIN")]
    [InlineData(null)]
    public void A_probe_with_each_admitted_permission_or_an_explicit_absence_is_accepted(string? permission)
    {
        Assert.True(GitHubRepositoryPermissionPolicy.IsAcceptedProbe(Probe(permission: permission), Target));
    }

    [Theory]
    [InlineData("")]
    [InlineData("write")]
    [InlineData("OWNER")]
    [InlineData("WRITE ")]
    [InlineData("TRIAGE_PLUS\n")]
    public void A_probe_with_an_unknown_permission_is_refused_whole_even_with_a_valid_identity(string permission)
    {
        Assert.False(GitHubRepositoryPermissionPolicy.IsAcceptedProbe(Probe(permission: permission), Target));
    }

    [Fact]
    public void A_missing_probe_is_refused()
    {
        Assert.False(GitHubRepositoryPermissionPolicy.IsAcceptedProbe(null, Target));
    }

    [Theory]
    [InlineData("")]
    [InlineData("R kg")]
    [InlineData("R.kg")]
    public void A_probe_with_a_malformed_node_id_is_refused(string nodeId)
    {
        Assert.False(GitHubRepositoryPermissionPolicy.IsAcceptedProbe(Probe(nodeId: nodeId), Target));
    }

    [Fact]
    public void A_node_id_with_a_non_ascii_letter_is_refused()
    {
        Assert.False(GitHubRepositoryPermissionPolicy.IsAcceptedProbe(Probe(nodeId: "R_kg" + char.ConvertFromUtf32(0xE9)), Target));
    }

    [Fact]
    public void A_node_id_at_the_length_bound_is_accepted_and_one_beyond_it_is_not()
    {
        Assert.True(GitHubRepositoryPermissionPolicy.IsAcceptedProbe(Probe(nodeId: new string('a', 256)), Target));
        Assert.False(GitHubRepositoryPermissionPolicy.IsAcceptedProbe(Probe(nodeId: new string('a', 257)), Target));
    }

    [Theory]
    [InlineData("OCTO-CAT", "HELLO.WORLD", true)]
    [InlineData("Octo-Cat", "Hello.World", true)]
    [InlineData("someone-else", "hello.world", false)]
    [InlineData("octo-cat", "renamed", false)]
    [InlineData("octo-cat", "hello.world.git", false)]
    [InlineData("octo-cat2", "hello.world", false)]
    [InlineData("-octo-cat", "hello.world", false)]
    [InlineData("octo-cat", ".hello.world", false)]
    [InlineData("octo-cat ", "hello.world", false)]
    [InlineData("octo-cat", "hello.world ", false)]
    [InlineData("", "hello.world", false)]
    [InlineData("octo-cat", "", false)]
    public void The_returned_names_must_equal_the_recorded_scope_ignoring_only_ascii_case(string owner, string repository, bool accepted)
    {
        Assert.Equal(accepted, GitHubRepositoryPermissionPolicy.IsAcceptedProbe(Probe(owner: owner, repository: repository), Target));
    }

    [Fact]
    public void The_kelvin_sign_and_other_non_ascii_case_folds_are_not_ascii_case_equal_names()
    {
        var target = Target with { Owner = "kelvin", Repository = "repo" };

        Assert.False(GitHubRepositoryPermissionPolicy.IsAcceptedProbe(Probe(owner: char.ConvertFromUtf32(0x212A) + "elvin", repository: "repo"), target));
    }
}
