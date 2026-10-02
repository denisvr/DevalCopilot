using System.Text;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.CreateVerificationDiagnosisAttempt;
using DevalCopilot.Application.Features.Runs.Policies.VerificationDiagnosis;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.DiagnosisTestScene;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The verified, bounded failure-output excerpts a diagnosis seals (ADR-0018): every failed stream is read through the
/// artifact store's verified read, an unverifiable one refuses and is never turned into an invented empty log, and the
/// deterministic prefix allocation honours the per-stream and total byte caps on whole code points with exact labels.
/// </summary>
public sealed class VerificationFailureExcerptsTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static Spec Failed(byte[] stdout, byte[] stderr, bool? truncated = false) =>
        new(Kind.Failed, Stdout: stdout, Stderr: stderr, Truncated: truncated);

    private static async Task<VerificationDiagnosisEvidence.Selection> SelectionAsync(DiagnosisTestScene scene)
    {
        await using var db = scene.Fixture.CreateContext();
        var read = await VerificationDiagnosisEvidence.ReadAsync(
            db, scene.Run.ProjectId, scene.Scene.Workspace.Id, scene.Implementation.ReviewCheckpoint, asNoTracking: true, CancellationToken.None);
        return read.Value!;
    }

    private static async Task<IReadOnlyList<VerificationFailureExcerpts.RawStream>> PrefixesAsync(DiagnosisTestScene scene)
    {
        var selection = await SelectionAsync(scene);
        var result = await VerificationFailureExcerpts.ReadVerifiedPrefixesAsync(scene.Store, selection, CancellationToken.None);
        Assert.Null(result.Error);
        return result.Streams!;
    }

    [Fact]
    public async Task Verified_prefixes_are_read_in_command_order_standard_output_before_standard_error_and_never_for_a_pass()
    {
        var scene = await CreateAsync(
            _fixture,
            [Failed(Bytes("one out"), Bytes("one err")), Spec.Passed(), Failed(Bytes("three out"), Bytes("three err"))]);

        var streams = await PrefixesAsync(scene);

        Assert.Equal(
            [
                (scene.Executions[0].Id, "standardOutput", "one out"),
                (scene.Executions[0].Id, "standardError", "one err"),
                (scene.Executions[2].Id, "standardOutput", "three out"),
                (scene.Executions[2].Id, "standardError", "three err"),
            ],
            streams.Select(s => (s.ExecutionId, s.Stream, s.PrefixText)));
        Assert.Equal(4, scene.Store.VerifyCalls);
        Assert.All(streams, s => Assert.True(s.PrefixCoversAll));
        Assert.All(streams, s => Assert.False(s.CaptureTruncated));
    }

    [Fact]
    public async Task A_missing_sealed_stream_refuses_the_claim_and_never_becomes_an_invented_empty_log()
    {
        var scene = await CreateAsync(_fixture, [Spec.Failed()]);
        var selection = await SelectionAsync(scene);
        scene.Store.Remove(selection.Entries[0].StandardError!.RelativeStoragePath);

        var result = await VerificationFailureExcerpts.ReadVerifiedPrefixesAsync(scene.Store, selection, CancellationToken.None);

        Assert.Null(result.Streams);
        Assert.Equal(VerificationDiagnosisEvidence.OutputUnavailableCode, result.Error!.Code);
    }

    [Fact]
    public async Task A_sealed_stream_that_fails_the_integrity_check_refuses_the_claim()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        var selection = await SelectionAsync(scene);
        scene.Store.Overwrite(selection.Entries[1].StandardOutput!.RelativeStoragePath, Bytes("tampered output of another length"));

        var result = await VerificationFailureExcerpts.ReadVerifiedPrefixesAsync(scene.Store, selection, CancellationToken.None);

        Assert.Null(result.Streams);
        Assert.Equal(VerificationDiagnosisEvidence.OutputUnavailableCode, result.Error!.Code);
    }

    [Fact]
    public async Task A_same_length_tampered_stream_is_caught_by_the_hash()
    {
        var scene = await CreateAsync(_fixture, [Failed(Bytes("abcdef"), Bytes("ghijkl"))]);
        var selection = await SelectionAsync(scene);
        scene.Store.Overwrite(selection.Entries[0].StandardOutput!.RelativeStoragePath, Bytes("abcdeX"));

        var result = await VerificationFailureExcerpts.ReadVerifiedPrefixesAsync(scene.Store, selection, CancellationToken.None);

        Assert.Equal(VerificationDiagnosisEvidence.OutputUnavailableCode, result.Error!.Code);
    }

    // ---- Per-stream and total caps --------------------------------------------------------------------------------------

    [Fact]
    public async Task A_stream_longer_than_two_kibibytes_is_a_shortened_two_kibibyte_prefix_with_its_full_captured_length()
    {
        var scene = await CreateAsync(_fixture, [Failed(Bytes(new string('a', 5000)), Bytes("small"))]);

        var excerpts = VerificationFailureExcerpts.Allocate(await PrefixesAsync(scene), VerificationFailureExcerpts.MaxTotalBytes);

        var stdout = excerpts[(scene.Executions[0].Id, "standardOutput")];
        Assert.Equal(2048, stdout.ExcerptBytes);
        Assert.Equal(new string('a', 2048), stdout.Text);
        Assert.Equal(5000, stdout.CapturedBytes);
        Assert.Equal(VerificationFailureExcerpts.StateShortened, stdout.ExcerptState);
        var stderr = excerpts[(scene.Executions[0].Id, "standardError")];
        Assert.Equal(VerificationFailureExcerpts.StateComplete, stderr.ExcerptState);
        Assert.Equal("small", stderr.Text);
    }

    [Theory]
    [InlineData(2047, "complete")]
    [InlineData(2048, "complete")]
    [InlineData(2049, "shortened")]
    public async Task The_complete_state_holds_exactly_when_the_whole_stream_fits_the_per_stream_cap(int length, string expectedState)
    {
        var scene = await CreateAsync(_fixture, [Failed(Bytes(new string('b', length)), Bytes("x"))]);

        var excerpts = VerificationFailureExcerpts.Allocate(await PrefixesAsync(scene), VerificationFailureExcerpts.MaxTotalBytes);

        var stdout = excerpts[(scene.Executions[0].Id, "standardOutput")];
        Assert.Equal(expectedState, stdout.ExcerptState);
        Assert.Equal(Math.Min(length, 2048), stdout.ExcerptBytes);
    }

    [Fact]
    public async Task An_empty_stream_is_labelled_empty_without_text()
    {
        var scene = await CreateAsync(_fixture, [Failed([], Bytes("only stderr"))]);

        var excerpts = VerificationFailureExcerpts.Allocate(await PrefixesAsync(scene), VerificationFailureExcerpts.MaxTotalBytes);

        var stdout = excerpts[(scene.Executions[0].Id, "standardOutput")];
        Assert.Equal(VerificationFailureExcerpts.StateEmpty, stdout.ExcerptState);
        Assert.Equal(string.Empty, stdout.Text);
        Assert.Equal(0, stdout.ExcerptBytes);
        Assert.Equal(0, stdout.CapturedBytes);
    }

    [Fact]
    public async Task The_total_budget_is_allocated_in_command_order_and_the_remainder_is_omitted_by_budget()
    {
        // Four failed commands, each stream longer than the per-stream cap: eight 2 KiB prefixes against a 12 KiB total.
        var big = Bytes(new string('z', 3000));
        var scene = await CreateAsync(_fixture, [Failed(big, big), Failed(big, big), Failed(big, big), Failed(big, big)]);

        var streams = await PrefixesAsync(scene);
        var excerpts = VerificationFailureExcerpts.Allocate(streams, VerificationFailureExcerpts.MaxTotalBytes);

        var ordered = streams.Select(s => excerpts[(s.ExecutionId, s.Stream)]).ToArray();
        Assert.Equal(8, ordered.Length);
        Assert.All(ordered.Take(6), e =>
        {
            Assert.Equal(VerificationFailureExcerpts.StateShortened, e.ExcerptState);
            Assert.Equal(2048, e.ExcerptBytes);
        });
        Assert.All(ordered.Skip(6), e =>
        {
            Assert.Equal(VerificationFailureExcerpts.StateOmittedByBudget, e.ExcerptState);
            Assert.Equal(0, e.ExcerptBytes);
            Assert.Equal(string.Empty, e.Text);
            Assert.Equal(3000, e.CapturedBytes);
        });
        Assert.True(ordered.Sum(e => e.ExcerptBytes) <= VerificationFailureExcerpts.MaxTotalBytes);
    }

    [Fact]
    public async Task The_last_stream_that_still_fits_is_cut_at_the_remaining_budget_and_shortened()
    {
        var big = Bytes(new string('y', 2048));
        var scene = await CreateAsync(_fixture, [Failed(big, big), Failed(big, big)]);

        var streams = await PrefixesAsync(scene);
        var excerpts = VerificationFailureExcerpts.Allocate(streams, 5000);

        var sizes = streams.Select(s => excerpts[(s.ExecutionId, s.Stream)].ExcerptBytes).ToArray();
        Assert.Equal([2048, 2048, 904, 0], sizes);
        Assert.Equal(
            ["complete", "complete", "shortened", "omittedByBudget"],
            streams.Select(s => excerpts[(s.ExecutionId, s.Stream)].ExcerptState));
    }

    [Fact]
    public async Task Allocation_is_deterministic_and_a_smaller_budget_is_always_a_prefix_of_a_larger_one()
    {
        var text = string.Concat(Enumerable.Range(0, 400).Select(i => $"line {i}\n"));
        var scene = await CreateAsync(_fixture, [Failed(Bytes(text), Bytes(text)), Failed(Bytes(text), Bytes(text))]);
        var streams = await PrefixesAsync(scene);

        var first = VerificationFailureExcerpts.Allocate(streams, 6000);
        var second = VerificationFailureExcerpts.Allocate(streams, 6000);
        var smaller = VerificationFailureExcerpts.Allocate(streams, 3000);

        foreach (var stream in streams)
        {
            var key = (stream.ExecutionId, stream.Stream);
            Assert.Equal(first[key], second[key]);
            Assert.StartsWith(smaller[key].Text, first[key].Text, StringComparison.Ordinal);
        }
    }

    // ---- Code points ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_multi_byte_character_straddling_the_per_stream_cap_is_dropped_whole_never_split()
    {
        // 2047 ASCII bytes then a two-byte character: the 2048-byte window ends inside the character.
        var scene = await CreateAsync(_fixture, [Failed(Bytes(new string('a', 2047) + "é" + "tail"), Bytes("e"))]);

        var excerpts = VerificationFailureExcerpts.Allocate(await PrefixesAsync(scene), VerificationFailureExcerpts.MaxTotalBytes);

        var stdout = excerpts[(scene.Executions[0].Id, "standardOutput")];
        Assert.Equal(new string('a', 2047), stdout.Text);
        Assert.DoesNotContain('�', stdout.Text);
        Assert.Equal(2047, stdout.ExcerptBytes);
        Assert.Equal(VerificationFailureExcerpts.StateShortened, stdout.ExcerptState);
    }

    [Theory]
    [InlineData("é", 2)]
    [InlineData("€", 3)]
    [InlineData("😀", 4)]
    public async Task A_multi_byte_character_is_cut_whole_at_the_total_budget(string character, int width)
    {
        var scene = await CreateAsync(_fixture, [Failed(Bytes(string.Concat(Enumerable.Repeat(character, 50))), Bytes("e"))]);
        var streams = await PrefixesAsync(scene);

        // A budget one byte short of two whole characters keeps exactly one.
        var excerpts = VerificationFailureExcerpts.Allocate(streams, (2 * width) - 1);

        var stdout = excerpts[(scene.Executions[0].Id, "standardOutput")];
        Assert.Equal(character, stdout.Text);
        Assert.Equal(width, stdout.ExcerptBytes);
        Assert.DoesNotContain('�', stdout.Text);
        Assert.Equal(VerificationFailureExcerpts.StateShortened, stdout.ExcerptState);
    }

    [Fact]
    public void A_budget_smaller_than_the_first_character_yields_an_empty_shortened_excerpt_not_a_partial_character()
    {
        var execution = Guid.NewGuid();
        var streams = new[] { new VerificationFailureExcerpts.RawStream(execution, "standardOutput", 3, false, "€", true) };

        var excerpt = VerificationFailureExcerpts.Allocate(streams, 2)[(execution, "standardOutput")];

        Assert.Equal(string.Empty, excerpt.Text);
        Assert.Equal(0, excerpt.ExcerptBytes);
        Assert.Equal(VerificationFailureExcerpts.StateShortened, excerpt.ExcerptState);
    }

    [Theory]
    [InlineData("abc", 3, "abc")]
    [InlineData("abc", 2, "ab")]
    [InlineData("aé", 2, "a")]
    [InlineData("aé", 3, "aé")]
    [InlineData("€€", 5, "€")]
    [InlineData("a😀", 4, "a")]
    [InlineData("a😀", 5, "a😀")]
    [InlineData("😀😀", 7, "😀")]
    [InlineData("😀", 0, "")]
    public void TruncateUtf8_returns_the_longest_whole_code_point_prefix_within_the_byte_limit(string text, int maxBytes, string expected)
    {
        var truncated = VerificationFailureExcerpts.TruncateUtf8(text, maxBytes);

        Assert.Equal(expected, truncated);
        Assert.True(Encoding.UTF8.GetByteCount(truncated) <= maxBytes);
    }

    // ---- Labels ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Capture_truncation_is_labelled_from_the_recorded_flag_and_unknown_after_a_recovered_capture()
    {
        var scene = await CreateAsync(
            _fixture,
            [
                Failed(Bytes("o1"), Bytes("e1"), truncated: false),
                Failed(Bytes("o2"), Bytes("e2"), truncated: true),
                new Spec(
                    Kind.Failed, Stdout: Bytes("o3"), Stderr: Bytes("e3"), Truncated: null,
                    Capture: VerificationOutputCaptureOutcome.RecoveredAfterHostInterruption),
            ]);

        var streams = await PrefixesAsync(scene);
        var excerpts = VerificationFailureExcerpts.Allocate(streams, VerificationFailureExcerpts.MaxTotalBytes);

        Assert.Equal(
            ["notTruncated", "notTruncated", "truncated", "truncated", "unknown", "unknown"],
            streams.Select(s => excerpts[(s.ExecutionId, s.Stream)].CaptureTruncation));
        Assert.Null(streams[4].CaptureTruncated);
    }

    [Fact]
    public async Task A_recovered_capture_is_unknown_even_if_a_truncation_value_were_recorded()
    {
        var scene = await CreateAsync(
            _fixture,
            [new Spec(Kind.Failed, Stdout: Bytes("o"), Stderr: Bytes("e"), Capture: VerificationOutputCaptureOutcome.RecoveredAfterHostInterruption)]);
        // The Domain refuses a recovered row with a truncation value, so a corrupt one is written directly.
        await using (var other = _fixture.CreateContext())
        {
            await other.VerificationOutputArtifacts.Where(o => o.VerificationExecutionId == scene.Executions[0].Id)
                .ExecuteUpdateAsync(set => set.SetProperty(o => o.Truncated, (bool?)true));
        }

        var streams = await PrefixesAsync(scene);

        Assert.All(streams, s => Assert.Null(s.CaptureTruncated));
    }

    // ---- Bounds ---------------------------------------------------------------------------------------------------

    /// <summary>The per-stream cap is a UTF-8 byte cap on the excerpt text. A sealed stream that is not valid UTF-8 decodes
    /// every invalid byte to a three-byte replacement character, so the 2048-byte window becomes a text of up to 6144 bytes
    /// that <see cref="VerificationFailureExcerpts.Allocate"/> must still cap at the per-stream limit.</summary>
    [Fact]
    public async Task Invalid_utf8_output_never_exceeds_the_per_stream_cap()
    {
        var scene = await CreateAsync(_fixture, [Failed(Enumerable.Repeat((byte)0xFF, 4000).ToArray(), Bytes("e"))]);

        var excerpts = VerificationFailureExcerpts.Allocate(await PrefixesAsync(scene), VerificationFailureExcerpts.MaxTotalBytes);

        var stdout = excerpts[(scene.Executions[0].Id, "standardOutput")];
        Assert.True(stdout.ExcerptBytes <= VerificationFailureExcerpts.MaxBytesPerStream, $"{stdout.ExcerptBytes} bytes");
    }
}
