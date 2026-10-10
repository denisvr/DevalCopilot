using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

public sealed class SealedEvidenceTests : IDisposable
{
    private static readonly byte[] Content = Encoding.UTF8.GetBytes("{\"sealed\":true}");

    private readonly string _base = Path.Combine(Path.GetTempPath(), $"devalcopilot-xunit-sealed-{Guid.NewGuid():N}");
    private readonly string _artifacts;

    public SealedEvidenceTests()
    {
        _artifacts = Directory.CreateDirectory(Path.Combine(_base, "artifacts")).FullName;
        Directory.CreateDirectory(Path.Combine(_artifacts, "run", "attempt"));
        File.WriteAllBytes(Path.Combine(_artifacts, "run", "attempt", "manifest.json"), Content);
        File.WriteAllBytes(Path.Combine(_base, "outside.json"), Content);
    }

    public void Dispose()
    {
        foreach (var junction in Directory.EnumerateDirectories(_base, "*", SearchOption.AllDirectories)
                     .Where(path => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)).ToArray())
        {
            Directory.Delete(junction);
        }

        Directory.Delete(_base, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static string Hash(byte[] bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));

    private byte[]? Read(string relative, long? length = null, string? hash = null, bool captured = true) =>
        SealedArtifactFile.ReadVerified(_artifacts, relative, length ?? Content.Length, hash ?? Hash(Content), captured);

    [Fact]
    public void A_whole_captured_file_with_the_recorded_length_and_hash_is_read()
    {
        Assert.Equal(Content, Read(Path.Combine("run", "attempt", "manifest.json")));
    }

    [Fact]
    public void Altered_bytes_a_wrong_length_or_a_wrong_hash_are_not_the_sealed_file()
    {
        var relative = Path.Combine("run", "attempt", "manifest.json");
        File.WriteAllBytes(Path.Combine(_artifacts, relative), Encoding.UTF8.GetBytes("{\"sealed\":false}"));

        Assert.Null(Read(relative));
        File.WriteAllBytes(Path.Combine(_artifacts, relative), Content);
        Assert.Null(Read(relative, length: Content.Length + 1));
        Assert.Null(Read(relative, hash: Hash(Encoding.UTF8.GetBytes("other"))));
        Assert.Null(Read(relative, hash: Hash(Content).ToUpperInvariant()));
        Assert.Equal(Content, Read(relative));
    }

    [Fact]
    public void A_partial_recovered_artifact_is_never_agreement()
    {
        Assert.Null(Read(Path.Combine("run", "attempt", "manifest.json"), captured: false));
    }

    [Theory]
    [InlineData("..", "outside.json")]
    [InlineData("run", "missing.json")]
    public void A_path_that_escapes_the_root_or_does_not_exist_is_not_read(string first, string second)
    {
        Assert.Null(Read(Path.Combine(first, second)));
    }

    [Fact]
    public void An_absolute_path_is_never_read_even_when_it_names_an_identical_file()
    {
        Assert.Null(Read(Path.Combine(_base, "outside.json")));
    }

    [Fact]
    public void A_file_reached_through_an_alias_is_not_read()
    {
        using (var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{Path.Combine(_artifacts, "alias")}\" \"{Path.Combine(_artifacts, "run")}\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!)
        {
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }

        Assert.Null(Read(Path.Combine("alias", "attempt", "manifest.json")));
        Assert.Equal(Content, Read(Path.Combine("run", "attempt", "manifest.json")));
    }

    private static byte[] Manifest(string json) => Encoding.UTF8.GetBytes(json);

    [Fact]
    public void A_planner_manifest_declares_its_contract_as_the_message_type_and_names_no_proposal()
    {
        var facts = SealedManifestReader.Read(Manifest("{\"expectedMessageType\":\"Proposal\",\"objective\":\"Plan it\"}"), "Proposal", "Plan it");

        Assert.True(facts.ContractAgrees);
        Assert.True(facts.ObjectiveAgrees);
        Assert.Null(facts.ProposalMessageId);
    }

    [Fact]
    public void A_review_manifest_declares_its_response_contract_and_names_the_reviewed_proposal()
    {
        var proposal = Guid.NewGuid();
        var json = $"{{\"expectedResponseContract\":\"CriticalReview\",\"objective\":\"Plan it\",\"reviewedProposal\":{{\"messageId\":\"{proposal}\"}}}}";

        var facts = SealedManifestReader.Read(Manifest(json), "CriticalReview", "Plan it");

        Assert.True(facts.ContractAgrees);
        Assert.True(facts.ObjectiveAgrees);
        Assert.Equal(proposal, facts.ProposalMessageId);
    }

    [Theory]
    [InlineData("{\"expectedMessageType\":\"Proposal\",\"objective\":\"Plan something else\"}", true, false)]
    [InlineData("{\"expectedMessageType\":\"Challenge\",\"objective\":\"Plan it\"}", false, true)]
    [InlineData("{\"expectedResponseContract\":\"Proposal\",\"objective\":\"Plan it\"}", false, true)]
    [InlineData("{\"objective\":\"Plan it\"}", false, true)]
    [InlineData("{\"expectedMessageType\":7,\"objective\":9}", false, false)]
    [InlineData("[]", false, false)]
    [InlineData("not json", false, false)]
    public void A_planner_manifest_with_another_contract_objective_or_shape_does_not_agree(string json, bool contract, bool objective)
    {
        var facts = SealedManifestReader.Read(Manifest(json), "Proposal", "Plan it");

        Assert.Equal(contract, facts.ContractAgrees);
        Assert.Equal(objective, facts.ObjectiveAgrees);
    }

    [Theory]
    [InlineData("{\"expectedResponseContract\":\"CriticalReview\",\"reviewedProposal\":\"not-an-object\"}")]
    [InlineData("{\"expectedResponseContract\":\"CriticalReview\",\"reviewedProposal\":{\"messageId\":\"not-a-guid\"}}")]
    [InlineData("{\"expectedResponseContract\":\"CriticalReview\",\"reviewedProposal\":{}}")]
    [InlineData("{\"expectedResponseContract\":\"CriticalReview\"}")]
    public void A_review_manifest_that_names_no_valid_proposal_identity_yields_none(string json)
    {
        Assert.Null(SealedManifestReader.Read(Manifest(json), "CriticalReview", "Plan it").ProposalMessageId);
    }
}
