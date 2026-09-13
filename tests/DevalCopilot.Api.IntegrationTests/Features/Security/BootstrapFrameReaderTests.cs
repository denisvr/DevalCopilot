using System.Text;
using DevalCopilot.Api.Security.Bootstrap;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Security;

public sealed class BootstrapFrameReaderTests
{
    // Exactly 64 lowercase hex characters, built by repetition rather than a hand-typed
    // literal so its length can't silently drift from what BootstrapFrameReader requires.
    private static readonly string ValidSecret = string.Concat(Enumerable.Repeat("0123456789abcdef", 4));

    private static MemoryStream StreamOf(string content) => new(Encoding.UTF8.GetBytes(content));

    [Fact]
    public async Task ReadAsync_accepts_a_valid_frame()
    {
        await using var stream = StreamOf($$"""{"version":1,"secret":"{{ValidSecret}}"}""" + "\n");

        var result = await BootstrapFrameReader.ReadAsync(stream, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Frame.Version);
        Assert.Equal(ValidSecret, result.Value.Frame.Secret);
        Assert.Empty(result.Value.Trailing);
    }

    [Fact]
    public async Task ReadAsync_rejects_an_unsupported_version()
    {
        await using var stream = StreamOf($$"""{"version":2,"secret":"{{ValidSecret}}"}""" + "\n");

        var result = await BootstrapFrameReader.ReadAsync(stream, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("bootstrap.unsupported_version", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task ReadAsync_rejects_malformed_json()
    {
        await using var stream = StreamOf("not json at all\n");

        var result = await BootstrapFrameReader.ReadAsync(stream, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("bootstrap.malformed", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task ReadAsync_rejects_a_frame_that_is_oversized()
    {
        var oversizedSecret = new string('a', 8192);
        await using var stream = StreamOf($$"""{"version":1,"secret":"{{oversizedSecret}}"}""" + "\n");

        var result = await BootstrapFrameReader.ReadAsync(stream, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("bootstrap.oversized", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task ReadAsync_rejects_a_missing_secret()
    {
        await using var stream = StreamOf("""{"version":1}""" + "\n");

        var result = await BootstrapFrameReader.ReadAsync(stream, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("bootstrap.malformed", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task ReadAsync_rejects_an_empty_secret()
    {
        await using var stream = StreamOf("""{"version":1,"secret":""}""" + "\n");

        var result = await BootstrapFrameReader.ReadAsync(stream, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("bootstrap.malformed", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task ReadAsync_fails_closed_when_the_input_ends_before_any_frame()
    {
        await using var stream = StreamOf(string.Empty);

        var result = await BootstrapFrameReader.ReadAsync(stream, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("bootstrap.missing", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task ReadAsync_fails_closed_when_the_input_ends_mid_frame()
    {
        await using var stream = StreamOf("""{"version":1,"secret":""");

        var result = await BootstrapFrameReader.ReadAsync(stream, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("bootstrap.incomplete", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task ReadAsync_surfaces_a_duplicated_frame_as_trailing_bytes_for_the_caller_to_reject()
    {
        var secondFrame = $$"""{"version":1,"secret":"{{ValidSecret}}"}""" + "\n";
        await using var stream = StreamOf($$"""{"version":1,"secret":"{{ValidSecret}}"}""" + "\n" + secondFrame);

        var result = await BootstrapFrameReader.ReadAsync(stream, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value.Trailing);
        Assert.Equal(secondFrame, Encoding.UTF8.GetString(result.Value.Trailing));
    }

    [Fact]
    public async Task ReadAsync_tolerates_a_leading_utf8_bom()
    {
        // A real shell (raw bytes, no encoding layer) never sends one, but some redirected-
        // stdin writers (e.g. a .NET Framework StreamWriter) prepend one by default.
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var content = Encoding.UTF8.GetBytes($$"""{"version":1,"secret":"{{ValidSecret}}"}""" + "\n");
        await using var stream = new MemoryStream([.. bom, .. content]);

        var result = await BootstrapFrameReader.ReadAsync(stream, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ValidSecret, result.Value.Frame.Secret);
    }
}
