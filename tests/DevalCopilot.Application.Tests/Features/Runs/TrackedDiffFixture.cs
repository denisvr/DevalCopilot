using System.Text;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Deterministic git-format patch text for manifest tests. Counts in every hunk header are computed
/// from the lines, so a fixture is always a well-formed patch unless a test deliberately corrupts it.</summary>
internal static class TrackedDiffFixture
{
    public static string Hunk(int oldStart, params string[] lines)
    {
        var oldCount = lines.Count(line => line.StartsWith(' ') || line.StartsWith('-'));
        var newCount = lines.Count(line => line.StartsWith(' ') || line.StartsWith('+'));
        var builder = new StringBuilder($"@@ -{oldStart},{oldCount} +{oldStart},{newCount} @@\n");
        foreach (var line in lines)
        {
            builder.Append(line).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>A hunk of <paramref name="changedLines"/> replaced lines, each padded to a fixed width.</summary>
    public static string LargeHunk(int oldStart, int changedLines, char fill, int width = 60)
    {
        var lines = new List<string>();
        for (var index = 0; index < changedLines; index++)
        {
            lines.Add("-" + new string(fill, width));
            lines.Add("+" + new string((char)(fill + 1), width));
        }

        return Hunk(oldStart, [.. lines]);
    }

    public static string TextFile(string path, params string[] hunks) =>
        $"diff --git a/{path} b/{path}\nindex 1111111..2222222 100644\n--- a/{path}\n+++ b/{path}\n{string.Concat(hunks)}";

    public static string QuotedTextFile(string quotedPath, params string[] hunks) =>
        $"diff --git \"a/{quotedPath}\" \"b/{quotedPath}\"\nindex 1111111..2222222 100644\n" +
        $"--- \"a/{quotedPath}\"\n+++ \"b/{quotedPath}\"\n{string.Concat(hunks)}";

    public static string BinaryFile(string path) =>
        $"diff --git a/{path} b/{path}\nindex 1111111..2222222 100644\nGIT binary patch\nliteral 8\nJcmZQzU|?VZ00Bw\n\nliteral 4\nHcmZQz00000\n\n";

    public static string ModeChange(string path) =>
        $"diff --git a/{path} b/{path}\nold mode 100644\nnew mode 100755\n";
}
