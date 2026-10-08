using DevalCopilot.Infrastructure.Features.Runs;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>Exercises the production FILE_RENAME_INFO encoder and native invocation seam. These tests intentionally never
/// reimplement the encoder: each payload originates at <see cref="WindowsIndexEffectHandles.CreateNoReplaceRenameBuffer"/>.</summary>
public sealed class WindowsIndexEffectHandlesRenameBufferTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "devalcopilot-rename-buffer-tests-" + Guid.NewGuid().ToString("N"));

    public WindowsIndexEffectHandlesRenameBufferTests() => Directory.CreateDirectory(root);

    [Theory]
    [InlineData("index")]
    [InlineData("index with spaces")]
    [InlineData("índice-漢字")]
    public void Production_buffer_terminates_the_exact_no_replace_destination(string leaf)
    {
        var source = Write("source", "owned");
        var destination = Path.Combine(root, leaf);
        using var handle = OpenForRename(source);

        var payload = WindowsIndexEffectHandles.CreateNoReplaceRenameBuffer(destination);
        var renamed = WindowsIndexEffectHandles.TrySetRenameInformation(handle, payload, out var error);
        handle.Dispose();

        Assert.True(renamed, $"native error {error}");
        Assert.True(File.Exists(destination));
        Assert.Equal("owned", File.ReadAllText(destination));
        Assert.False(File.Exists(source));
        Assert.Equal((int)payload.FileNameLength + 2, payload.Bytes.Length - ((IntPtr.Size * 2) + sizeof(uint)));
        Assert.Equal(0, payload.Bytes[^1]);
        Assert.Equal(0, payload.Bytes[^2]);
    }

    [Fact]
    public void Production_buffer_preserves_a_foreign_collision_without_replacement()
    {
        var source = Write("source", "owned");
        var destination = Write("index", "foreign");
        using var handle = OpenForRename(source);

        var renamed = WindowsIndexEffectHandles.TrySetRenameInformation(
            handle, WindowsIndexEffectHandles.CreateNoReplaceRenameBuffer(destination), out var error);
        handle.Dispose();

        Assert.False(renamed);
        Assert.Equal(183, error);
        Assert.Equal("owned", File.ReadAllText(source));
        Assert.Equal("foreign", File.ReadAllText(destination));
    }

    [Fact]
    public void Removing_the_production_buffers_terminator_is_detected_before_native_effect()
    {
        var source = Write("source", "owned");
        var destination = Path.Combine(root, "index");
        using var handle = OpenForRename(source);
        var payload = WindowsIndexEffectHandles.CreateNoReplaceRenameBuffer(destination);
        payload.Bytes[^1] = (byte)'X';

        var renamed = WindowsIndexEffectHandles.TrySetRenameInformation(handle, payload, out var error);

        Assert.False(renamed);
        Assert.Equal(87, error);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(destination));
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static Microsoft.Win32.SafeHandles.SafeFileHandle OpenForRename(string path) =>
        WindowsIndexEffectHandles.OpenRenameSourceForTest(path);

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
