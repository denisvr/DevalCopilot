using DevalCopilot.Infrastructure.Features.Runs;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>
/// First-use initialization of the host-owned local-commit control storage (ADR-0029): the empty Git configuration file and the
/// proven-empty hooks directory. Overlapping initializers are interleaved at exact boundaries through the storage's observation
/// seam over real files and independent storage instances; invalid or occupied state is refused without being changed.
/// </summary>
public sealed class LocalCommitStorageInitializationTests : IDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);
    private const string Sentinel = "[core]\n\thooksPath = sentinel\n";

    private readonly string _parent = Path.Combine(Path.GetTempPath(), $"devalcopilot-storage-init-{Guid.NewGuid():N}");
    private readonly List<IDisposable> _holds = [];

    public LocalCommitStorageInitializationTests() => Directory.CreateDirectory(_parent);

    public void Dispose()
    {
        foreach (var hold in _holds)
        {
            hold.Dispose();
        }

        foreach (var file in Directory.EnumerateFiles(_parent, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_parent, recursive: true);
    }

    private string NewRoot() => Path.Combine(_parent, Guid.NewGuid().ToString("N"), "local-commit");

    private static LocalCommitStorage StorageAt(string root, Action<string>? observer = null) =>
        new(root) { InitializationObserver = observer };

    private static byte[] Bytes(string path) => File.ReadAllBytes(path);

    /// <summary>Parks the one initializer that reaches <paramref name="phase"/> until the test releases it. Disposal releases it too,
    /// so a failing assertion can never leave a thread blocked.</summary>
    private sealed class Hold(string phase) : IDisposable
    {
        private readonly ManualResetEventSlim _reached = new(false);
        private readonly ManualResetEventSlim _release = new(false);

        public void Observe(string observed)
        {
            if (observed != phase || _reached.IsSet)
            {
                return;
            }

            _reached.Set();
            if (!_release.Wait(Bound))
            {
                throw new TimeoutException($"The hold at {phase} was never released.");
            }
        }

        public void AwaitReached() => Assert.True(_reached.Wait(Bound), $"no initializer reached {phase}");

        public void Release() => _release.Set();

        public void Dispose() => _release.Set();
    }

    private Hold HoldAt(string phase)
    {
        var hold = new Hold(phase);
        _holds.Add(hold);
        return hold;
    }

    [Fact]
    public void A_fresh_root_is_initialized_with_an_empty_configuration_and_an_empty_hooks_directory()
    {
        var storage = StorageAt(NewRoot());

        Assert.True(storage.TryEnsureHooksDirectoryEmpty());

        Assert.True(File.Exists(storage.EmptyConfigPath));
        Assert.Empty(Bytes(storage.EmptyConfigPath));
        Assert.True(Directory.Exists(storage.HooksDirectory));
        Assert.Empty(Directory.EnumerateFileSystemEntries(storage.HooksDirectory));
    }

    [Fact]
    public async Task An_initializer_that_decided_to_create_the_file_tolerates_a_creator_that_still_holds_it_open()
    {
        var root = NewRoot();
        var hold = HoldAt("configuration_opening");
        var late = StorageAt(root, hold.Observe);
        var initialization = Task.Run(late.TryEnsureHooksDirectoryEmpty);
        hold.AwaitReached();

        // Another initializer is mid-creation: it holds the new file open for writing and allows readers, exactly as the previous
        // File.WriteAllBytes did while it wrote.
        var creator = new FileStream(late.EmptyConfigPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        _holds.Add(creator);
        hold.Release();

        Assert.True(await initialization.WaitAsync(Bound));
        creator.Dispose();
        Assert.Empty(Bytes(late.EmptyConfigPath));
    }

    [Fact]
    public async Task An_initializer_overlapping_another_that_holds_the_opened_configuration_both_succeed()
    {
        var root = NewRoot();
        var hold = HoldAt("configuration_opened");
        var first = StorageAt(root, hold.Observe);
        var firstResult = Task.Run(first.TryEnsureHooksDirectoryEmpty);
        hold.AwaitReached();

        var second = StorageAt(root);
        Assert.True(second.TryEnsureHooksDirectoryEmpty());
        hold.Release();

        Assert.True(await firstResult.WaitAsync(Bound));
        Assert.Empty(Bytes(first.EmptyConfigPath));
    }

    [Fact]
    public async Task An_initializer_that_decided_to_create_after_another_already_created_the_file_keeps_it_empty_and_succeeds()
    {
        var root = NewRoot();
        var hold = HoldAt("configuration_opening");
        var late = StorageAt(root, hold.Observe);
        var initialization = Task.Run(late.TryEnsureHooksDirectoryEmpty);
        hold.AwaitReached();

        Assert.True(StorageAt(root).TryEnsureHooksDirectoryEmpty());
        hold.Release();

        Assert.True(await initialization.WaitAsync(Bound));
        Assert.Empty(Bytes(late.EmptyConfigPath));
    }

    [Fact]
    public async Task Initializers_released_together_at_the_creation_boundary_all_succeed_on_every_fresh_root()
    {
        for (var round = 0; round < 40; round++)
        {
            var root = NewRoot();
            using var arrived = new CountdownEvent(2);
            void Meet(string phase)
            {
                if (phase != "configuration_opening")
                {
                    return;
                }

                arrived.Signal();
                if (!arrived.Wait(Bound))
                {
                    throw new TimeoutException("the second initializer never reached the creation boundary");
                }
            }

            var a = StorageAt(root, Meet);
            var b = StorageAt(root, Meet);
            var results = await Task.WhenAll(
                Task.Factory.StartNew(a.TryEnsureHooksDirectoryEmpty, TaskCreationOptions.LongRunning),
                Task.Factory.StartNew(b.TryEnsureHooksDirectoryEmpty, TaskCreationOptions.LongRunning)).WaitAsync(Bound);

            Assert.True(results.All(result => result), $"round {round}: {string.Join(",", results)}");
            Assert.Empty(Bytes(a.EmptyConfigPath));
        }
    }

    [Fact]
    public void An_existing_empty_configuration_is_accepted_without_being_touched_even_when_read_only()
    {
        var storage = StorageAt(NewRoot());
        Directory.CreateDirectory(storage.HooksDirectory);
        File.WriteAllBytes(storage.EmptyConfigPath, []);
        var stamp = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(storage.EmptyConfigPath, stamp);
        File.SetAttributes(storage.EmptyConfigPath, FileAttributes.ReadOnly);

        Assert.True(storage.TryEnsureHooksDirectoryEmpty());

        Assert.Empty(Bytes(storage.EmptyConfigPath));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(storage.EmptyConfigPath));
        Assert.True(File.GetAttributes(storage.EmptyConfigPath).HasFlag(FileAttributes.ReadOnly));
    }

    [Fact]
    public void An_existing_non_empty_configuration_is_refused_and_its_bytes_and_timestamp_are_preserved()
    {
        var storage = StorageAt(NewRoot());
        Directory.CreateDirectory(storage.Root);
        File.WriteAllText(storage.EmptyConfigPath, Sentinel);
        var before = Bytes(storage.EmptyConfigPath);
        var stamp = File.GetLastWriteTimeUtc(storage.EmptyConfigPath);

        Assert.False(storage.TryEnsureHooksDirectoryEmpty());

        Assert.Equal(before, Bytes(storage.EmptyConfigPath));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(storage.EmptyConfigPath));
    }

    [Fact]
    public void A_directory_at_the_configuration_path_is_refused_and_left_in_place()
    {
        var storage = StorageAt(NewRoot());
        Directory.CreateDirectory(storage.EmptyConfigPath);
        File.WriteAllText(Path.Combine(storage.EmptyConfigPath, "inside.txt"), Sentinel);

        Assert.False(storage.TryEnsureHooksDirectoryEmpty());

        Assert.Equal(Sentinel, File.ReadAllText(Path.Combine(storage.EmptyConfigPath, "inside.txt")));
    }

    [Fact]
    public void A_configuration_another_process_holds_exclusively_is_refused_not_assumed_and_is_accepted_once_released()
    {
        var storage = StorageAt(NewRoot());
        Directory.CreateDirectory(storage.Root);
        File.WriteAllBytes(storage.EmptyConfigPath, []);
        var exclusive = new FileStream(storage.EmptyConfigPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        _holds.Add(exclusive);

        Assert.False(storage.TryEnsureHooksDirectoryEmpty());

        exclusive.Dispose();
        Assert.True(storage.TryEnsureHooksDirectoryEmpty());
        Assert.Empty(Bytes(storage.EmptyConfigPath));
    }

    [Fact]
    public void Any_hooks_entry_refuses_without_changing_it_and_the_refusal_is_not_remembered()
    {
        var storage = StorageAt(NewRoot());
        Assert.True(storage.TryEnsureHooksDirectoryEmpty());
        var hook = Path.Combine(storage.HooksDirectory, "reference-transaction");
        File.WriteAllText(hook, Sentinel);
        var folder = Path.Combine(storage.HooksDirectory, "nested");
        Directory.CreateDirectory(folder);

        Assert.False(storage.TryEnsureHooksDirectoryEmpty());
        Assert.False(StorageAt(storage.Root).TryEnsureHooksDirectoryEmpty());
        Assert.Equal(Sentinel, File.ReadAllText(hook));
        Assert.True(Directory.Exists(folder));

        File.Delete(hook);
        Directory.Delete(folder);
        Assert.True(storage.TryEnsureHooksDirectoryEmpty());
    }

    [Fact]
    public void A_file_where_the_hooks_directory_belongs_or_where_the_root_belongs_is_refused_and_left_in_place()
    {
        var occupiedHooks = StorageAt(NewRoot());
        Directory.CreateDirectory(occupiedHooks.Root);
        File.WriteAllText(occupiedHooks.HooksDirectory, Sentinel);
        Assert.False(occupiedHooks.TryEnsureHooksDirectoryEmpty());
        Assert.Equal(Sentinel, File.ReadAllText(occupiedHooks.HooksDirectory));

        var occupiedRoot = StorageAt(NewRoot());
        Directory.CreateDirectory(Path.GetDirectoryName(occupiedRoot.Root)!);
        File.WriteAllText(occupiedRoot.Root, Sentinel);
        Assert.False(occupiedRoot.TryEnsureHooksDirectoryEmpty());
        Assert.Equal(Sentinel, File.ReadAllText(occupiedRoot.Root));
    }

    [Fact]
    public void A_configuration_that_becomes_non_empty_after_a_successful_initialization_is_refused_on_the_next_call_unchanged()
    {
        var storage = StorageAt(NewRoot());
        Assert.True(storage.TryEnsureHooksDirectoryEmpty());
        File.WriteAllText(storage.EmptyConfigPath, Sentinel);

        Assert.False(storage.TryEnsureHooksDirectoryEmpty());
        Assert.False(StorageAt(storage.Root).TryEnsureHooksDirectoryEmpty());

        Assert.Equal(Sentinel, File.ReadAllText(storage.EmptyConfigPath));
    }

    [Fact]
    public void A_configuration_replaced_by_a_directory_after_a_successful_initialization_is_refused_on_the_next_call()
    {
        var storage = StorageAt(NewRoot());
        Assert.True(storage.TryEnsureHooksDirectoryEmpty());
        File.Delete(storage.EmptyConfigPath);
        Directory.CreateDirectory(storage.EmptyConfigPath);

        Assert.False(storage.TryEnsureHooksDirectoryEmpty());

        Assert.True(Directory.Exists(storage.EmptyConfigPath));
    }

    [Fact]
    public void A_hook_inserted_after_a_successful_initialization_is_refused_on_the_next_call()
    {
        var storage = StorageAt(NewRoot());
        Assert.True(storage.TryEnsureHooksDirectoryEmpty());
        Assert.True(storage.TryEnsureHooksDirectoryEmpty());
        File.WriteAllText(Path.Combine(storage.HooksDirectory, "pre-commit"), Sentinel);

        Assert.False(storage.TryEnsureHooksDirectoryEmpty());
    }

    [Fact]
    public void A_configuration_that_is_deleted_after_initialization_is_recreated_empty_and_accepted()
    {
        var storage = StorageAt(NewRoot());
        Assert.True(storage.TryEnsureHooksDirectoryEmpty());
        File.Delete(storage.EmptyConfigPath);

        Assert.True(storage.TryEnsureHooksDirectoryEmpty());

        Assert.Empty(Bytes(storage.EmptyConfigPath));
    }

    [Fact]
    public async Task A_configuration_that_becomes_non_empty_while_an_initializer_holds_it_open_is_refused_not_trusted()
    {
        var root = NewRoot();
        var hold = HoldAt("configuration_opened");
        var storage = StorageAt(root, hold.Observe);
        var initialization = Task.Run(storage.TryEnsureHooksDirectoryEmpty);
        hold.AwaitReached();

        // A competitor writes into the file after this initializer created it but before it finished: the decision is made from
        // the bytes the handle actually reports, never from the fact that this initializer created the file.
        File.WriteAllText(storage.EmptyConfigPath, Sentinel);
        hold.Release();

        Assert.False(await initialization.WaitAsync(Bound));
        Assert.Equal(Sentinel, File.ReadAllText(storage.EmptyConfigPath));
    }
}
