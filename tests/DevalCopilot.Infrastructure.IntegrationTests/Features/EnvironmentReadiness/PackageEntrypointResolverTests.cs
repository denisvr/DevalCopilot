using DevalCopilot.Infrastructure.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;

/// <summary>
/// Real filesystem, entirely inside a disposable temp directory — never the real
/// <c>%AppData%\npm</c> global install location, so this never depends on, or mutates, anything
/// actually installed on the machine running the tests. Node resolution goes through
/// <see cref="ProviderNodeHostResolver"/>, which never reads <c>PATH</c> at all, so a fabricated
/// candidate name here is only to avoid colliding with a real <c>node.exe</c> that might already
/// exist in one of the fixed host roots this test constructs — never to avoid a PATH match, which
/// this resolver cannot produce.
/// </summary>
public sealed class PackageEntrypointResolverTests : IDisposable
{
    private const string ExpectedPackageName = "@openai/codex";
    private const string ExpectedCommandName = "codex";

    private readonly string _rootDirectory =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-package-entrypoint-{Guid.NewGuid():N}");
    private readonly string _nodeDirectory;
    private readonly string _nodeCandidateName = $"devalcopilot-fake-node-{Guid.NewGuid():N}.exe";

    public PackageEntrypointResolverTests()
    {
        Directory.CreateDirectory(_rootDirectory);
        _nodeDirectory = Path.Combine(_rootDirectory, "node");
        Directory.CreateDirectory(_nodeDirectory);
        File.WriteAllText(Path.Combine(_nodeDirectory, _nodeCandidateName), string.Empty);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private IReadOnlyList<string> NodeCandidateNames => [_nodeCandidateName];

    private IReadOnlyList<string> NodeFallbackDirectories => [_nodeDirectory];

    private IReadOnlyList<string> NoNodeFallbackDirectories { get; } = [];

    private string CreatePackageRoot(string subdirectory, string? packageJson, string? scriptRelativePath, string scriptContent = "")
    {
        var packageRoot = Path.Combine(_rootDirectory, subdirectory);
        Directory.CreateDirectory(packageRoot);

        if (packageJson is not null)
        {
            File.WriteAllText(Path.Combine(packageRoot, "package.json"), packageJson);
        }

        if (scriptRelativePath is not null)
        {
            var scriptPath = Path.Combine(packageRoot, scriptRelativePath);
            var scriptDirectory = Path.GetDirectoryName(scriptPath)!;
            Directory.CreateDirectory(scriptDirectory);
            File.WriteAllText(scriptPath, scriptContent);
        }

        return packageRoot;
    }

    private static PackageEntrypointDescriptor Descriptor(params string[] packageRootCandidates) => new()
    {
        PackageRootCandidates = packageRootCandidates,
        ExpectedPackageName = ExpectedPackageName,
        ExpectedCommandName = ExpectedCommandName,
    };

    [Fact]
    public void Resolve_finds_the_script_for_a_valid_string_form_bin_matching_the_unscoped_package_name()
    {
        var root = CreatePackageRoot(
            "string-bin",
            """{"name": "@openai/codex", "bin": "bin/codex.js"}""",
            Path.Combine("bin", "codex.js"));

        var result = PackageEntrypointResolver.Resolve(Descriptor(root), NodeCandidateNames, NodeFallbackDirectories);

        Assert.Equal(PackageEntrypointResolutionKind.Resolved, result.Kind);
        var nodeScript = Assert.IsType<ProviderLaunchTarget.NodeScript>(result.Target);
        Assert.Equal(Path.Combine(root, "bin", "codex.js"), nodeScript.ScriptPath);
        Assert.EndsWith(_nodeCandidateName, nodeScript.NodeExecutablePath);
    }

    // The real-world case this resolver did not originally handle: "@anthropic-ai/claude-code"
    // ships a "bin" entry that, after a normal install, is replaced with an actual native
    // executable — never a JavaScript file requiring a Node host. Running it *through* Node
    // would try to parse a PE binary as JavaScript source and fail.
    [Fact]
    public void Resolve_treats_an_exe_suffixed_bin_target_with_a_real_pe_header_as_a_direct_executable()
    {
        var root = CreatePackageRootWithBinaryScript(
            "native-exe-bin",
            """{"name": "@anthropic-ai/claude-code", "bin": {"claude": "bin/claude.exe"}}""",
            Path.Combine("bin", "claude.exe"),
            CreateMinimalValidPeBytes());

        var descriptor = new PackageEntrypointDescriptor
        {
            PackageRootCandidates = [root],
            ExpectedPackageName = "@anthropic-ai/claude-code",
            ExpectedCommandName = "claude",
        };
        var result = PackageEntrypointResolver.Resolve(descriptor, NodeCandidateNames, NodeFallbackDirectories);

        Assert.Equal(PackageEntrypointResolutionKind.Resolved, result.Kind);
        var direct = Assert.IsType<ProviderLaunchTarget.DirectExecutable>(result.Target);
        Assert.Equal(Path.Combine(root, "bin", "claude.exe"), direct.ExecutablePath);
    }

    // Extension alone is never trusted: a ".exe"-suffixed file whose content is not actually a
    // PE binary fails closed to NotFound — it is never silently re-interpreted as a JavaScript
    // entrypoint and run through Node either, since that would be equally unsafe.
    [Fact]
    public void Resolve_fails_closed_when_an_exe_suffixed_bin_target_lacks_a_real_pe_header()
    {
        var root = CreatePackageRootWithBinaryScript(
            "fake-exe-bin",
            """{"name": "@anthropic-ai/claude-code", "bin": {"claude": "bin/claude.exe"}}""",
            Path.Combine("bin", "claude.exe"),
            "#!/usr/bin/env node\nconsole.log('not actually a PE binary');"u8.ToArray());

        var descriptor = new PackageEntrypointDescriptor
        {
            PackageRootCandidates = [root],
            ExpectedPackageName = "@anthropic-ai/claude-code",
            ExpectedCommandName = "claude",
        };
        var result = PackageEntrypointResolver.Resolve(descriptor, NodeCandidateNames, NodeFallbackDirectories);

        Assert.Equal(PackageEntrypointResolutionKind.NotFound, result.Kind);
    }

    // The exact regression this hardening closes: a bare two-byte "MZ" signature alone used to
    // be trusted as proof of a native executable. A file that genuinely starts with "MZ" and is
    // long enough to carry a plausible e_lfanew pointer, but whose e_lfanew points at bytes that
    // are not the literal "PE\0\0" signature, must still fail closed — "MZ" is only the DOS stub
    // marker, never sufficient proof by itself.
    [Fact]
    public void Resolve_fails_closed_when_an_exe_suffixed_bin_target_has_the_mz_signature_but_no_valid_pe_signature_at_e_lfanew()
    {
        var bytes = new byte[0x44];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        // e_lfanew = 0x40, little-endian, at the fixed DOS-header offset 0x3C.
        bytes[0x3C] = 0x40;
        bytes[0x3D] = 0x00;
        bytes[0x3E] = 0x00;
        bytes[0x3F] = 0x00;
        // Bytes at 0x40 are deliberately NOT "PE\0\0".
        bytes[0x40] = (byte)'X';
        bytes[0x41] = (byte)'X';
        bytes[0x42] = 0x00;
        bytes[0x43] = 0x00;

        var root = CreatePackageRootWithBinaryScript(
            "mz-only-exe-bin",
            """{"name": "@anthropic-ai/claude-code", "bin": {"claude": "bin/claude.exe"}}""",
            Path.Combine("bin", "claude.exe"),
            bytes);

        var descriptor = new PackageEntrypointDescriptor
        {
            PackageRootCandidates = [root],
            ExpectedPackageName = "@anthropic-ai/claude-code",
            ExpectedCommandName = "claude",
        };
        var result = PackageEntrypointResolver.Resolve(descriptor, NodeCandidateNames, NodeFallbackDirectories);

        Assert.Equal(PackageEntrypointResolutionKind.NotFound, result.Kind);
    }

    // A malformed e_lfanew that points past the end of the (short) file must also fail closed
    // rather than let a bounded read silently come up short and be misinterpreted.
    [Fact]
    public void Resolve_fails_closed_when_an_exe_suffixed_bin_target_has_an_e_lfanew_pointer_past_the_end_of_the_file()
    {
        var bytes = new byte[0x40];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        // e_lfanew = 0x1000: far past this 0x40-byte file.
        bytes[0x3C] = 0x00;
        bytes[0x3D] = 0x10;
        bytes[0x3E] = 0x00;
        bytes[0x3F] = 0x00;

        var root = CreatePackageRootWithBinaryScript(
            "mz-out-of-range-exe-bin",
            """{"name": "@anthropic-ai/claude-code", "bin": {"claude": "bin/claude.exe"}}""",
            Path.Combine("bin", "claude.exe"),
            bytes);

        var descriptor = new PackageEntrypointDescriptor
        {
            PackageRootCandidates = [root],
            ExpectedPackageName = "@anthropic-ai/claude-code",
            ExpectedCommandName = "claude",
        };
        var result = PackageEntrypointResolver.Resolve(descriptor, NodeCandidateNames, NodeFallbackDirectories);

        Assert.Equal(PackageEntrypointResolutionKind.NotFound, result.Kind);
    }

    /// <summary>The smallest byte sequence this resolver's structural check actually requires to
    /// accept a file as a genuine native Windows executable: "MZ" at offset 0, a valid
    /// <c>e_lfanew</c> at offset 0x3C pointing immediately past the minimal 64-byte DOS header,
    /// and a literal "PE\0\0" signature at that offset. Never a real, loadable PE image (no COFF
    /// or optional header) — this resolver never inspects anything beyond these three facts.</summary>
    private static byte[] CreateMinimalValidPeBytes()
    {
        const int PeHeaderOffset = 0x40;
        var bytes = new byte[PeHeaderOffset + 4];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        bytes[0x3C] = PeHeaderOffset;
        bytes[0x3D] = 0x00;
        bytes[0x3E] = 0x00;
        bytes[0x3F] = 0x00;
        bytes[PeHeaderOffset] = (byte)'P';
        bytes[PeHeaderOffset + 1] = (byte)'E';
        bytes[PeHeaderOffset + 2] = 0x00;
        bytes[PeHeaderOffset + 3] = 0x00;
        return bytes;
    }

    private string CreatePackageRootWithBinaryScript(
        string subdirectory, string packageJson, string scriptRelativePath, byte[] scriptContent)
    {
        var packageRoot = Path.Combine(_rootDirectory, subdirectory);
        Directory.CreateDirectory(packageRoot);
        File.WriteAllText(Path.Combine(packageRoot, "package.json"), packageJson);

        var scriptPath = Path.Combine(packageRoot, scriptRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        File.WriteAllBytes(scriptPath, scriptContent);

        return packageRoot;
    }

    [Fact]
    public void Resolve_finds_the_script_for_a_valid_object_form_bin_selecting_the_exact_expected_command()
    {
        var root = CreatePackageRoot(
            "object-bin",
            """{"name": "@openai/codex", "bin": {"codex": "cli.js", "codex-other": "other.js"}}""",
            "cli.js");

        var result = PackageEntrypointResolver.Resolve(Descriptor(root), NodeCandidateNames, NodeFallbackDirectories);

        Assert.Equal(PackageEntrypointResolutionKind.Resolved, result.Kind);
        var nodeScript = Assert.IsType<ProviderLaunchTarget.NodeScript>(result.Target);
        Assert.Equal(Path.Combine(root, "cli.js"), nodeScript.ScriptPath);
    }

    [Fact]
    public void Resolve_ignores_a_manifest_naming_a_different_package()
    {
        var root = CreatePackageRoot(
            "wrong-package",
            """{"name": "@someone-else/tool", "bin": "cli.js"}""",
            "cli.js");

        var result = PackageEntrypointResolver.Resolve(Descriptor(root), NodeCandidateNames, NodeFallbackDirectories);

        Assert.Equal(PackageEntrypointResolutionKind.NotFound, result.Kind);
    }

    [Fact]
    public void Resolve_ignores_malformed_json()
    {
        var root = CreatePackageRoot("malformed", "{ this is not valid json", "cli.js");

        var result = PackageEntrypointResolver.Resolve(Descriptor(root), NodeCandidateNames, NodeFallbackDirectories);

        Assert.Equal(PackageEntrypointResolutionKind.NotFound, result.Kind);
    }

    [Fact]
    public void Resolve_ignores_an_oversized_manifest()
    {
        var root = CreatePackageRoot("oversized", null, "cli.js");
        var padding = new string('x', 70 * 1024);
        File.WriteAllText(
            Path.Combine(root, "package.json"),
            $$"""{"name": "@openai/codex", "bin": "cli.js", "padding": "{{padding}}"}""");

        var result = PackageEntrypointResolver.Resolve(Descriptor(root), NodeCandidateNames, NodeFallbackDirectories);

        Assert.Equal(PackageEntrypointResolutionKind.NotFound, result.Kind);
    }

    [Fact]
    public void Resolve_ignores_a_missing_manifest()
    {
        var root = CreatePackageRoot("missing-manifest", packageJson: null, scriptRelativePath: "cli.js");

        var result = PackageEntrypointResolver.Resolve(Descriptor(root), NodeCandidateNames, NodeFallbackDirectories);

        Assert.Equal(PackageEntrypointResolutionKind.NotFound, result.Kind);
    }

    // The manifest size bound is enforced by how many bytes the bounded stream read actually
    // reads (capped at MaxPackageJsonBytes + 1), never by a separate FileInfo.Length check
    // followed by a full unbounded read — closing the check-then-read race a size pre-check
    // would otherwise leave open. A genuine concurrent grow-during-read is not something a unit
    // test can deterministically force without racing the OS's own file-system cache; the three
    // tests below instead prove the bound is exact and never partial, which is what the fix
    // actually changed.

    [Fact]
    public void Resolve_accepts_a_manifest_exactly_at_the_size_limit()
    {
        const int MaxPackageJsonBytes = 64 * 1024;
        var root = CreatePackageRoot("exactly-at-limit", packageJson: null, "cli.js");
        File.WriteAllText(Path.Combine(root, "package.json"), BuildManifestOfExactByteLength(MaxPackageJsonBytes));

        var result = PackageEntrypointResolver.Resolve(Descriptor(root), NodeCandidateNames, NodeFallbackDirectories);

        Assert.Equal(PackageEntrypointResolutionKind.Resolved, result.Kind);
    }

    [Fact]
    public void Resolve_rejects_a_manifest_exactly_one_byte_over_the_size_limit()
    {
        const int MaxPackageJsonBytes = 64 * 1024;
        var root = CreatePackageRoot("one-byte-over-limit", packageJson: null, "cli.js");
        File.WriteAllText(Path.Combine(root, "package.json"), BuildManifestOfExactByteLength(MaxPackageJsonBytes + 1));

        var result = PackageEntrypointResolver.Resolve(Descriptor(root), NodeCandidateNames, NodeFallbackDirectories);

        Assert.Equal(PackageEntrypointResolutionKind.NotFound, result.Kind);
    }

    [Fact]
    public void Resolve_never_partially_parses_an_over_limit_manifest_even_when_its_first_bytes_alone_would_be_valid()
    {
        const int MaxPackageJsonBytes = 64 * 1024;
        var root = CreatePackageRoot("over-limit-with-valid-prefix", packageJson: null, "cli.js");
        // The first MaxPackageJsonBytes characters alone are already a complete, syntactically
        // valid, matching manifest. If the bound were enforced by truncating an oversized file
        // down to its first N bytes and parsing only those, this candidate would wrongly
        // resolve; the fix reads the file whole (bounded) and rejects the whole thing instead.
        var exactlyAtLimit = BuildManifestOfExactByteLength(MaxPackageJsonBytes);
        File.WriteAllText(Path.Combine(root, "package.json"), exactlyAtLimit + " ");

        var result = PackageEntrypointResolver.Resolve(Descriptor(root), NodeCandidateNames, NodeFallbackDirectories);

        Assert.Equal(PackageEntrypointResolutionKind.NotFound, result.Kind);
    }

    /// <summary>Builds a valid, matching manifest of exactly <paramref name="targetByteLength"/>
    /// ASCII bytes by padding an ignored extra field — ASCII keeps char count and UTF-8 byte
    /// count identical, so the arithmetic is exact.</summary>
    private static string BuildManifestOfExactByteLength(int targetByteLength)
    {
        string BuildWithPadding(int padLength) =>
            $$"""{"name": "@openai/codex", "bin": "cli.js", "padding": "{{new string('x', Math.Max(padLength, 0))}}"}""";

        var baseLength = BuildWithPadding(0).Length;
        return BuildWithPadding(targetByteLength - baseLength);
    }

    [Fact]
    public void Resolve_ignores_a_manifest_with_no_bin_field()
    {
        var root = CreatePackageRoot("no-bin", """{"name": "@openai/codex"}""", "cli.js");

        var result = PackageEntrypointResolver.Resolve(Descriptor(root), NodeCandidateNames, NodeFallbackDirectories);

        Assert.Equal(PackageEntrypointResolutionKind.NotFound, result.Kind);
    }

    [Fact]
    public void Resolve_ignores_an_object_form_bin_that_does_not_contain_the_expected_command()
    {
        var root = CreatePackageRoot(
            "wrong-command",
            """{"name": "@openai/codex", "bin": {"something-else": "cli.js"}}""",
            "cli.js");

        var result = PackageEntrypointResolver.Resolve(Descriptor(root), NodeCandidateNames, NodeFallbackDirectories);

        Assert.Equal(PackageEntrypointResolutionKind.NotFound, result.Kind);
    }

    [Fact]
    public void Resolve_ignores_a_string_form_bin_that_does_not_match_the_expected_command()
    {
        // "name" resolves to unscoped command "not-codex", which is not this provider's expected
        // command — a string-form "bin" is only unambiguous when it names the expected command.
        var root = CreatePackageRoot(
            "string-bin-mismatch",
            """{"name": "@openai/not-codex", "bin": "cli.js"}""",
            "cli.js");

        var result = PackageEntrypointResolver.Resolve(
            new PackageEntrypointDescriptor
            {
                PackageRootCandidates = [root],
                ExpectedPackageName = "@openai/not-codex",
                ExpectedCommandName = ExpectedCommandName,
            },
            NodeCandidateNames,
            NodeFallbackDirectories);

        Assert.Equal(PackageEntrypointResolutionKind.NotFound, result.Kind);
    }

    [Fact]
    public void Resolve_rejects_a_bin_path_that_escapes_the_package_root()
    {
        // The referenced file genuinely exists (just outside the package root) — proving the
        // rejection is the containment check itself, not a missing-file coincidence.
        File.WriteAllText(Path.Combine(_rootDirectory, "escaped.js"), string.Empty);
        var root = CreatePackageRoot(
            "traversal",
            """{"name": "@openai/codex", "bin": "../escaped.js"}""",
            scriptRelativePath: null);

        var result = PackageEntrypointResolver.Resolve(Descriptor(root), NodeCandidateNames, NodeFallbackDirectories);

        Assert.Equal(PackageEntrypointResolutionKind.NotFound, result.Kind);
    }

    [Fact]
    public void Resolve_rejects_an_absolute_bin_path()
    {
        var absoluteScript = Path.Combine(_rootDirectory, "absolute-script.js");
        File.WriteAllText(absoluteScript, string.Empty);
        var root = CreatePackageRoot(
            "absolute-bin",
            $$"""{"name": "@openai/codex", "bin": "{{absoluteScript.Replace("\\", "\\\\")}}"}""",
            scriptRelativePath: null);

        var result = PackageEntrypointResolver.Resolve(Descriptor(root), NodeCandidateNames, NodeFallbackDirectories);

        Assert.Equal(PackageEntrypointResolutionKind.NotFound, result.Kind);
    }

    [Fact]
    public void Resolve_ignores_a_bin_entry_pointing_to_a_file_that_does_not_exist()
    {
        var root = CreatePackageRoot(
            "missing-script",
            """{"name": "@openai/codex", "bin": "cli.js"}""",
            scriptRelativePath: null);

        var result = PackageEntrypointResolver.Resolve(Descriptor(root), NodeCandidateNames, NodeFallbackDirectories);

        Assert.Equal(PackageEntrypointResolutionKind.NotFound, result.Kind);
    }

    [Fact]
    public void Resolve_is_ambiguous_when_two_candidate_roots_each_resolve_a_distinct_script()
    {
        var first = CreatePackageRoot("candidate-a", """{"name": "@openai/codex", "bin": "cli.js"}""", "cli.js");
        var second = CreatePackageRoot("candidate-b", """{"name": "@openai/codex", "bin": "cli.js"}""", "cli.js");

        var result = PackageEntrypointResolver.Resolve(Descriptor(first, second), NodeCandidateNames, NodeFallbackDirectories);

        Assert.Equal(PackageEntrypointResolutionKind.Ambiguous, result.Kind);
        Assert.Null(result.Target);
    }

    [Fact]
    public void Resolve_is_not_ambiguous_when_two_candidate_roots_resolve_the_same_canonical_script()
    {
        var root = CreatePackageRoot("single-candidate", """{"name": "@openai/codex", "bin": "cli.js"}""", "cli.js");
        // The same root reachable via two spellings — proves the dedup is on canonical identity,
        // not raw candidate-string identity.
        var rootViaDotSegment = Path.Combine(_rootDirectory, "single-candidate", ".");

        var result = PackageEntrypointResolver.Resolve(
            Descriptor(root, rootViaDotSegment), NodeCandidateNames, NodeFallbackDirectories);

        Assert.Equal(PackageEntrypointResolutionKind.Resolved, result.Kind);
    }

    [Fact]
    public void Resolve_returns_not_found_when_the_script_resolves_but_no_direct_node_executable_does()
    {
        var root = CreatePackageRoot("no-node", """{"name": "@openai/codex", "bin": "cli.js"}""", "cli.js");

        var result = PackageEntrypointResolver.Resolve(Descriptor(root), NodeCandidateNames, NoNodeFallbackDirectories);

        Assert.Equal(PackageEntrypointResolutionKind.NotFound, result.Kind);
    }

    [Fact]
    public void Resolve_returns_not_found_when_no_candidate_root_exists_at_all()
    {
        var nonExistentRoot = Path.Combine(_rootDirectory, $"nonexistent-{Guid.NewGuid():N}");

        var result = PackageEntrypointResolver.Resolve(Descriptor(nonExistentRoot), NodeCandidateNames, NodeFallbackDirectories);

        Assert.Equal(PackageEntrypointResolutionKind.NotFound, result.Kind);
    }

    // Reparse-point rejection: lexical containment (Path.GetFullPath) proves nothing once a
    // junction or symbolic link sits on the chain between the package root and the entrypoint —
    // the OS can physically open a completely different location than the string implies. These
    // tests use the same non-elevated fixture techniques already established and reviewed in
    // RepositoryPhysicalIdentityInspectorTests (a directory junction via plain `mklink /J`, which
    // needs no elevation on modern Windows) and RepositoryRootPathInspectorTests (an attempted
    // symbolic link, gracefully skipped via early return if this host lacks the privilege/
    // Developer Mode a file-level symlink requires — junctions are directory-only in NTFS, so
    // there is no non-elevated way to make a single *file* a reparse point).
    //
    // None of these rejections ever reach IProcessExecutionAdapter: PackageEntrypointResolver
    // holds no reference to that port at all, so "no process is launched for a rejected target"
    // is a structural property of this class, not something a mock needs to assert per case.

    [Fact]
    public void Resolve_succeeds_for_a_normal_package_with_no_reparse_points_anywhere_on_the_chain()
    {
        var root = CreatePackageRoot(
            "normal", """{"name": "@openai/codex", "bin": "bin/codex.js"}""", Path.Combine("bin", "codex.js"));

        var result = PackageEntrypointResolver.Resolve(Descriptor(root), NodeCandidateNames, NodeFallbackDirectories);

        Assert.Equal(PackageEntrypointResolutionKind.Resolved, result.Kind);
    }

    [WindowsOnlyFact]
    public void Resolve_rejects_a_package_root_that_is_itself_a_reparse_point()
    {
        var realRoot = CreatePackageRoot("real-root", """{"name": "@openai/codex", "bin": "cli.js"}""", "cli.js");
        var junctionRoot = Path.Combine(_rootDirectory, "junction-root");

        var junctionCreation = TryCreateJunction(junctionRoot, realRoot);
        try
        {
            // Directory junction creation needs no elevation on Windows (verified empirically
            // for this suite) — a failure here is a genuine environmental problem worth failing
            // loudly over, never a reason to silently treat the security assertion below as
            // satisfied.
            Assert.True(
                junctionCreation.Succeeded,
                $"Directory junction creation failed (exit code {junctionCreation.ExitCode}): {junctionCreation.StandardError}");

            var result = PackageEntrypointResolver.Resolve(Descriptor(junctionRoot), NodeCandidateNames, NodeFallbackDirectories);

            Assert.Equal(PackageEntrypointResolutionKind.NotFound, result.Kind);
        }
        finally
        {
            // The junction may or may not exist depending on exactly how creation failed above;
            // cleanup must not itself throw and mask the real assertion failure.
            if (Directory.Exists(junctionRoot))
            {
                Directory.Delete(junctionRoot);
            }
        }
    }

    [WindowsOnlyFact]
    public void Resolve_rejects_an_entrypoint_reached_through_a_reparse_point_intermediate_directory()
    {
        var root = CreatePackageRoot("intermediate-root", """{"name": "@openai/codex", "bin": "sub/cli.js"}""", scriptRelativePath: null);
        var realSubdirectory = Path.Combine(_rootDirectory, "real-subdirectory");
        Directory.CreateDirectory(realSubdirectory);
        File.WriteAllText(Path.Combine(realSubdirectory, "cli.js"), string.Empty);
        var junctionSubdirectory = Path.Combine(root, "sub");

        var junctionCreation = TryCreateJunction(junctionSubdirectory, realSubdirectory);
        try
        {
            Assert.True(
                junctionCreation.Succeeded,
                $"Directory junction creation failed (exit code {junctionCreation.ExitCode}): {junctionCreation.StandardError}");

            var result = PackageEntrypointResolver.Resolve(Descriptor(root), NodeCandidateNames, NodeFallbackDirectories);

            Assert.Equal(PackageEntrypointResolutionKind.NotFound, result.Kind);
        }
        finally
        {
            if (Directory.Exists(junctionSubdirectory))
            {
                Directory.Delete(junctionSubdirectory);
            }
        }
    }

    [RequiresFileSymlinkSupportFact]
    public void Resolve_rejects_an_entrypoint_that_is_itself_a_reparse_point()
    {
        var root = CreatePackageRoot("symlink-entrypoint-root", """{"name": "@openai/codex", "bin": "link.js"}""", scriptRelativePath: null);
        var realScript = Path.Combine(root, "real.js");
        File.WriteAllText(realScript, string.Empty);
        var linkScript = Path.Combine(root, "link.js");

        // The attribute above already proved this host can create a file symlink; a failure here
        // would be a genuine, newly-introduced regression, not an expected environmental gap.
        File.CreateSymbolicLink(linkScript, realScript);

        var result = PackageEntrypointResolver.Resolve(Descriptor(root), NodeCandidateNames, NodeFallbackDirectories);

        Assert.Equal(PackageEntrypointResolutionKind.NotFound, result.Kind);
    }

    /// <summary>Never leaks either the junction or target path: only the process's own exit code
    /// and stderr text are surfaced to a failing assertion.</summary>
    private readonly record struct JunctionCreationResult(bool Succeeded, int ExitCode, string StandardError);

    private static JunctionCreationResult TryCreateJunction(string junctionPath, string targetPath)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("mklink");
            startInfo.ArgumentList.Add("/J");
            startInfo.ArgumentList.Add(junctionPath);
            startInfo.ArgumentList.Add(targetPath);

            using var process = System.Diagnostics.Process.Start(startInfo)!;
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            var succeeded = process.ExitCode == 0 && Directory.Exists(junctionPath);
            return new JunctionCreationResult(succeeded, process.ExitCode, standardError);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new JunctionCreationResult(false, -1, exception.GetType().Name);
        }
    }
}
