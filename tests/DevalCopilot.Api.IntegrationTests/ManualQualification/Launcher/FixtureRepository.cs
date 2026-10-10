using System.Text;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>The tiny fixed fictitious repository the session plans and reviews: one stub source file, a readme, and two benign root
/// instruction files, committed once on main. Written as exact LF bytes, with no line-ending conversion, so the host and the
/// snapshots agree on every byte. Setup Git writes belong only to this directory.</summary>
public static class FixtureRepository
{
    public const string ProjectName = "Manual qualification fixture";
    public const string Objective = "Plan how Feature.Total should return the sum of its two integer arguments.";

    private static readonly (string Path, string Text)[] Files =
    [
        ("README.md", "# Manual qualification fixture\n"),
        ("AGENTS.md", "# Fixture conventions\n\nKeep every change inside src/Feature.cs.\n"),
        ("CLAUDE.md", "Notes for Claude: this is a disposable fictitious repository.\n"),
        (
            "src/Feature.cs",
            "namespace Fixture;\n\npublic static class Feature\n{\n"
            + "    public static int Total(int left, int right)\n    {\n        return 0;\n    }\n}\n"),
    ];

    public static string Create(string directory)
    {
        foreach (var (relative, text) in Files)
        {
            var path = Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(text));
        }

        GitProcess.Run(directory, "init", "--initial-branch", "main");
        GitProcess.Run(directory, "config", "core.autocrlf", "false");
        GitProcess.Run(directory, ["add", "--", .. Files.Select(file => file.Path)]);
        GitProcess.Run(
            directory,
            "-c", "user.name=Fixture Author",
            "-c", "user.email=fixture@example.invalid",
            "-c", "commit.gpgsign=false",
            "commit", "--no-verify", "-m", "Initial commit");
        return GitProcess.Run(directory, "rev-parse", "HEAD").Trim();
    }
}
