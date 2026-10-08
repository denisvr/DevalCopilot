using System.Runtime.InteropServices;
using System.Security.Cryptography;
using DevalCopilot.Infrastructure.Features.Runs;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>
/// The Windows physical effect boundary of ADR-0029, driven directly: exclusive acquisition through live handles, the HEAD, index,
/// artifact and lock protections, the two no-replace effects with their namespace confirmations (and the missing-index boundary
/// between them), full volume and 128-bit file identities, and disposal that only closes handles.
/// </summary>
public sealed class WindowsIndexEffectHandlesTests : IDisposable
{
    private const string Branch = "devalcopilot/workspace/test/1";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-handles-{Guid.NewGuid():N}");
    private readonly List<WindowsIndexEffectHandles> _held = [];

    public WindowsIndexEffectHandlesTests()
    {
        Directory.CreateDirectory(AdminDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(ArtifactPath)!);
        File.WriteAllText(HeadPath, $"ref: refs/heads/{Branch}\n");
        File.WriteAllBytes(IndexPath, Bytes(1, 300));
        File.WriteAllBytes(ArtifactPath, Bytes(2, 300));
    }

    private string AdminDirectory => Path.Combine(_root, "admin");

    private string HeadPath => Path.Combine(AdminDirectory, "HEAD");

    private string IndexPath => Path.Combine(AdminDirectory, "index");

    private string LockPath => IndexPath + ".lock";

    private string ArtifactPath => Path.Combine(_root, "artifacts", "prepared.index");

    private static byte[] Bytes(byte seed, int length) => Enumerable.Range(0, length).Select(index => (byte)(seed + index)).ToArray();

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public void Dispose()
    {
        foreach (var handles in _held)
        {
            handles.Dispose();
        }

        if (Directory.Exists(_root))
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_root, recursive: true);
        }
    }

    private (WindowsIndexEffectHandles Handles, WindowsIndexEffectHandles.Receipt Receipt)? TryAcquire(string branch = Branch)
    {
        var acquired = WindowsIndexEffectHandles.TryAcquire(
            AdminDirectory, ArtifactPath, Sha256(File.ReadAllBytes(IndexPath)), Sha256(File.ReadAllBytes(ArtifactPath)), branch,
            out var handles, out var receipt);
        if (!acquired)
        {
            return null;
        }

        _held.Add(handles!);
        return (handles!, receipt!);
    }

    private (WindowsIndexEffectHandles Handles, WindowsIndexEffectHandles.Receipt Receipt) Acquire() =>
        TryAcquire() ?? throw new Xunit.Sdk.XunitException("The acquisition was refused.");

    [Fact]
    public void Acquisition_creates_the_lock_with_the_prepared_bytes_and_records_full_physical_identities()
    {
        var (_, receipt) = Acquire();

        Assert.Equal(File.ReadAllBytes(ArtifactPath), ReadShared(LockPath));
        Assert.Equal("index.lock", receipt.LockName);
        Assert.Equal(300, receipt.LockLength);
        foreach (var identity in new[]
        {
            receipt.AdministrativeDirectoryIdentity, receipt.PreimageIdentity, receipt.PreparedArtifactIdentity, receipt.LockIdentity,
        })
        {
            Assert.Matches("^[0-9a-f]{16}:[0-9a-f]{32}$", identity);
        }

        Assert.Equal(4, new[]
        {
            receipt.AdministrativeDirectoryIdentity, receipt.PreimageIdentity, receipt.PreparedArtifactIdentity, receipt.LockIdentity,
        }.Distinct().Count());
    }

    [Fact]
    public void Every_acquired_object_refuses_other_writers_deleters_and_renamers_while_reads_still_work()
    {
        Acquire();
        var movedAdmin = AdminDirectory + "-moved";

        Assert.ThrowsAny<IOException>(() => File.WriteAllText(HeadPath, "ref: refs/heads/other\n"));
        Assert.ThrowsAny<IOException>(() => File.WriteAllBytes(IndexPath, [1, 2, 3]));
        Assert.ThrowsAny<IOException>(() => File.WriteAllBytes(LockPath, [1, 2, 3]));
        Assert.ThrowsAny<IOException>(() => File.WriteAllBytes(ArtifactPath, [1, 2, 3]));
        Assert.ThrowsAny<Exception>(() => File.Delete(HeadPath));
        Assert.ThrowsAny<Exception>(() => File.Delete(IndexPath));
        Assert.ThrowsAny<Exception>(() => File.Delete(LockPath));
        Assert.ThrowsAny<Exception>(() => File.Delete(ArtifactPath));
        Assert.ThrowsAny<IOException>(() => File.Move(IndexPath, IndexPath + ".moved"));
        Assert.ThrowsAny<IOException>(() => File.Move(HeadPath, HeadPath + ".moved"));
        Assert.ThrowsAny<IOException>(() => File.Move(IndexPath, LockPath + ".x"));
        Assert.ThrowsAny<IOException>(() => Directory.Move(AdminDirectory, movedAdmin));

        Assert.Equal(300, ReadShared(IndexPath).Length);
        Assert.Equal($"ref: refs/heads/{Branch}\n", Encoding(HeadPath));
        Assert.False(Directory.Exists(movedAdmin));
    }

    [Fact]
    public void A_foreign_lock_that_exists_blocks_acquisition_even_when_it_holds_identical_prepared_bytes()
    {
        File.Copy(ArtifactPath, LockPath);

        Assert.Null(TryAcquire());

        Assert.Equal(File.ReadAllBytes(ArtifactPath), File.ReadAllBytes(LockPath));
    }

    [Theory]
    [InlineData("changed index")]
    [InlineData("changed artifact")]
    public void A_changed_preimage_or_artifact_refuses_acquisition_without_creating_a_lock(string change)
    {
        var indexSha = Sha256(File.ReadAllBytes(IndexPath));
        var artifactSha = Sha256(File.ReadAllBytes(ArtifactPath));
        File.AppendAllBytes(change == "changed index" ? IndexPath : ArtifactPath, [0]);

        var acquired = WindowsIndexEffectHandles.TryAcquire(
            AdminDirectory, ArtifactPath, indexSha, artifactSha, Branch, out var handles, out _);

        Assert.False(acquired);
        Assert.Null(handles);
        Assert.False(File.Exists(LockPath));
    }

    [Theory]
    [InlineData("ref: refs/heads/other\n")]
    [InlineData("0123456789012345678901234567890123456789\n")]
    [InlineData("ref: refs/heads/devalcopilot/workspace/test/1")]
    [InlineData("ref: refs/heads/devalcopilot/workspace/test/1\r\n")]
    public void A_head_that_is_not_the_exact_literal_binding_refuses_acquisition_before_any_lock_exists(string head)
    {
        File.WriteAllText(HeadPath, head);

        Assert.Null(TryAcquire());
        Assert.False(File.Exists(LockPath));
    }

    [Fact]
    public void An_oversized_head_a_hard_linked_index_and_an_oversized_index_are_refused_before_any_lock_exists()
    {
        File.WriteAllText(HeadPath, "ref: refs/heads/" + new string('x', 2000) + "\n");
        Assert.Null(TryAcquire(new string('x', 2000)));
        File.WriteAllText(HeadPath, $"ref: refs/heads/{Branch}\n");

        var alias = Path.Combine(_root, "alias-of-index");
        Assert.True(CreateHardLinkW(alias, IndexPath, IntPtr.Zero), "the hard link control could not be created");
        Assert.Null(TryAcquire());
        File.Delete(alias);

        File.WriteAllBytes(IndexPath, new byte[WindowsIndexEffectHandles.MaximumIndexBytes + 1]);
        Assert.Null(TryAcquire());
        Assert.False(File.Exists(LockPath));
    }

    [Fact]
    public void The_live_head_proof_rereads_the_exact_literal_binding_and_rejects_every_other_branch()
    {
        var (handles, _) = Acquire();

        Assert.True(handles.IsHeadBoundTo(Branch));
        Assert.False(handles.IsHeadBoundTo(Branch + "2"));
        Assert.False(handles.IsHeadBoundTo("other"));
        Assert.True(handles.Verify());
    }

    [Fact]
    public void The_receipt_compares_the_full_volume_and_128_bit_file_identity_never_a_truncation()
    {
        var (handles, receipt) = Acquire();

        Assert.True(handles.IsReceipt(receipt));
        // The identity is "volume(16 hex):fileId(32 hex)". Changing only the HIGH 64 bits of the file id (the part a 64-bit
        // FileIndex ignores), the file id's low bits, or the volume must each fail the comparison.
        var parts = receipt.LockIdentity.Split(':');
        var volume = parts[0];
        var fileId = parts[1];
        string Flip(string value, int index) => value[..index] + (value[index] == '0' ? '1' : '0') + value[(index + 1)..];
        Assert.False(handles.IsReceipt(receipt with { LockIdentity = $"{volume}:{Flip(fileId, 0)}" }));
        Assert.False(handles.IsReceipt(receipt with { LockIdentity = $"{volume}:{Flip(fileId, 15)}" }));
        Assert.False(handles.IsReceipt(receipt with { LockIdentity = $"{volume}:{Flip(fileId, 31)}" }));
        Assert.False(handles.IsReceipt(receipt with { LockIdentity = $"{Flip(volume, 0)}:{fileId}" }));
        Assert.False(handles.IsReceipt(receipt with { PreimageIdentity = receipt.PreparedArtifactIdentity }));
        Assert.False(handles.IsReceipt(receipt with { LockLength = receipt.LockLength + 1 }));
        Assert.False(handles.IsReceipt(receipt with { LockName = "index.lock.other" }));
    }

    [Fact]
    public void Promotion_makes_the_prepared_lock_the_index_by_two_confirmed_no_replace_effects_and_removes_the_preimage_by_handle()
    {
        var preparedBytes = File.ReadAllBytes(ArtifactPath);
        var (handles, _) = Acquire();
        const string quarantine = "devalcopilot-test.index-preimage";

        Assert.True(handles.TryPromote(quarantine), handles.LastFailure + ":" + handles.LastNativeError);

        Assert.Equal(preparedBytes, ReadShared(IndexPath));
        Assert.False(File.Exists(LockPath));
        Assert.True(handles.TryReadPromotedIndex(out var promoted));
        Assert.Equal(preparedBytes, promoted);
        handles.Dispose();
        Assert.False(File.Exists(Path.Combine(AdminDirectory, quarantine)), "the held preimage is deleted by its own handle");
        Assert.Equal(preparedBytes, File.ReadAllBytes(IndexPath));
    }

    [Fact]
    public void An_occupied_quarantine_name_is_a_conflict_that_overwrites_and_moves_nothing()
    {
        var preimage = File.ReadAllBytes(IndexPath);
        var (handles, _) = Acquire();
        const string quarantine = "devalcopilot-test.index-preimage";
        File.WriteAllText(Path.Combine(AdminDirectory, quarantine), "foreign occupant");

        Assert.False(handles.TryPromote(quarantine));

        Assert.Equal("quarantine_rename", handles.LastFailure);
        Assert.Equal(183, handles.LastNativeError);
        Assert.Equal("foreign occupant", File.ReadAllText(Path.Combine(AdminDirectory, quarantine)));
        Assert.Equal(preimage, ReadShared(IndexPath));
        Assert.True(File.Exists(LockPath));
    }

    [Fact]
    public void A_foreign_index_appearing_in_the_vacant_interval_between_the_effects_survives_unchanged()
    {
        var preimage = File.ReadAllBytes(IndexPath);
        var (handles, _) = Acquire();
        const string quarantine = "devalcopilot-test.index-preimage";
        var observedVacant = false;
        handles.BetweenEffects = () =>
        {
            observedVacant = !File.Exists(IndexPath);
            File.WriteAllText(IndexPath, "a foreign process wrote this index");
        };

        Assert.False(handles.TryPromote(quarantine));

        Assert.True(observedVacant, "the real index name is vacant between the two effects");
        Assert.Equal("publish_rename", handles.LastFailure);
        Assert.Equal(183, handles.LastNativeError);
        Assert.Equal("a foreign process wrote this index", File.ReadAllText(IndexPath));
        Assert.Equal(preimage, ReadShared(Path.Combine(AdminDirectory, quarantine)));
        Assert.True(File.Exists(LockPath), "the held lock is neither published nor removed after a conflict");
    }

    [Fact]
    public void Releasing_the_held_lock_deletes_only_that_lock_by_handle_and_disposal_alone_removes_nothing()
    {
        var preimage = File.ReadAllBytes(IndexPath);
        var (handles, _) = Acquire();
        handles.Dispose();

        // Disposal only closes handles: the lock this operation created is still physically present, never silently removed.
        Assert.True(File.Exists(LockPath));
        File.Delete(LockPath);

        var (again, _) = Acquire();
        Assert.True(again.TryDeleteHeldLock());
        again.Dispose();

        Assert.False(File.Exists(LockPath));
        Assert.Equal(preimage, File.ReadAllBytes(IndexPath));
    }

    [Fact]
    public void A_disposed_acquisition_proves_nothing_and_refuses_every_effect()
    {
        var (handles, receipt) = Acquire();
        handles.Dispose();

        Assert.False(handles.IsReceipt(receipt));
        Assert.False(handles.Verify());
        Assert.False(handles.IsHeadBoundTo(Branch));
        Assert.False(handles.TryPromote("devalcopilot-test.index-preimage"));
        Assert.False(handles.TryDeleteHeldLock());
        Assert.False(handles.TryReadPromotedIndex(out _));
    }

    private static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static string Encoding(string path) => System.Text.Encoding.UTF8.GetString(ReadShared(path));

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string newFileName, string existingFileName, IntPtr securityAttributes);
}
