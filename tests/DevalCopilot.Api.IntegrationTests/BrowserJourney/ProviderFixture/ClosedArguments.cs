using System.Globalization;

namespace DevalCopilot.Api.IntegrationTests.BrowserJourney.ProviderFixture;

/// <summary>
/// The closed argument contracts the real adapters send. A double accepts exactly these shapes and refuses everything else, so
/// an adapter that gained a flag, lost a safety option, or used another subcommand fails the journey instead of being served.
/// </summary>
public static class ClosedArguments
{
    public const string CodexProbe = "--version";

    /// <summary>The Codex exec contract: the fixed prefix, an optional model and reasoning-effort pair, and the stdin marker.</summary>
    public sealed record CodexExec(string SchemaPath, string ResultPath, string WorkingDirectory);

    public enum ClaudeProfile
    {
        /// <summary>Critical review: no tools, plan permission mode, one turn.</summary>
        ReadOnly,

        /// <summary>Implementation and review correction: file tools, accept-edits mode, optional turn limit.</summary>
        Mutating,
    }

    public sealed record ClaudeExec(ClaudeProfile Profile, string SchemaJson);

    /// <summary>The one App Server launch the strict account-usage observation makes: exactly <c>app-server --stdio</c>.</summary>
    public static bool IsCodexAppServer(IReadOnlyList<string> args) =>
        args.Count == 2 && args[0] == "app-server" && args[1] == "--stdio";

    public static CodexExec ParseCodex(IReadOnlyList<string> args)
    {
        var index = 0;
        string Next() => index < args.Count ? args[index++] : throw Refuse("The Codex invocation ended early.");
        void Expect(string literal)
        {
            if (Next() != literal)
            {
                throw Refuse("The Codex invocation does not match the fixed contract.");
            }
        }

        Expect("exec");
        Expect("--json");
        Expect("--output-schema");
        var schema = Next();
        Expect("--output-last-message");
        var result = Next();
        Expect("--sandbox");
        Expect("read-only");
        Expect("--cd");
        var workingDirectory = Next();
        Expect("--ephemeral");
        Expect("--ignore-user-config");
        if (index < args.Count && args[index] == "--model")
        {
            index++;
            RequireIdentifier(Next());
        }

        if (index < args.Count && args[index] == "--config")
        {
            index++;
            var assignment = Next();
            if (!assignment.StartsWith("model_reasoning_effort=", StringComparison.Ordinal))
            {
                throw Refuse("Only the reasoning-effort configuration is accepted.");
            }

            RequireIdentifier(assignment["model_reasoning_effort=".Length..]);
        }

        Expect("-");
        if (index != args.Count)
        {
            throw Refuse("The Codex invocation carries unexpected arguments.");
        }

        return new CodexExec(schema, result, workingDirectory);
    }

    public static ClaudeExec ParseClaude(IReadOnlyList<string> args)
    {
        var index = 0;
        string Next() => index < args.Count ? args[index++] : throw Refuse("The Claude invocation ended early.");
        void Expect(string literal)
        {
            if (Next() != literal)
            {
                throw Refuse("The Claude invocation does not match the fixed contract.");
            }
        }

        Expect("--print");
        Expect("--input-format");
        Expect("text");
        Expect("--output-format");
        Expect("json");
        Expect("--json-schema");
        var schema = Next();
        Expect("--safe-mode");
        Expect("--restricted");
        Expect("--disable-slash-commands");
        Expect("--no-chrome");
        Expect("--permission-prompts");
        Expect("none");
        Expect("--prompt-suggestions");
        Expect("false");
        Expect("--tools");
        var tools = Next();
        Expect("--strict-mcp-config");
        Expect("--permission-mode");
        var mode = Next();
        Expect("--no-session-persistence");
        Expect("--session-id");
        if (!Guid.TryParseExact(Next(), "D", out _))
        {
            throw Refuse("The session identifier is not a GUID.");
        }

        var profile = (tools, mode) switch
        {
            ("", "plan") => ClaudeProfile.ReadOnly,
            ("Read,Edit,Write,Glob,Grep", "acceptEdits") => ClaudeProfile.Mutating,
            _ => throw Refuse("The tool and permission combination is not a known contract."),
        };

        if (index < args.Count && args[index] == "--model")
        {
            index++;
            RequireIdentifier(Next());
            if (index < args.Count && args[index] == "--effort")
            {
                index++;
                RequireIdentifier(Next());
            }
        }

        if (index < args.Count && args[index] == "--max-turns")
        {
            index++;
            if (!int.TryParse(Next(), NumberStyles.None, CultureInfo.InvariantCulture, out var turns) || turns < 1)
            {
                throw Refuse("The turn limit is not a positive integer.");
            }
        }

        if (index != args.Count)
        {
            throw Refuse("The Claude invocation carries unexpected arguments.");
        }

        if (string.IsNullOrWhiteSpace(schema))
        {
            throw Refuse("The response schema is missing.");
        }

        return new ClaudeExec(profile, schema);
    }

    private static void RequireIdentifier(string value)
    {
        if (value.Length is 0 or > 128 || !value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
        {
            throw Refuse("A model or effort identifier is not in the accepted character set.");
        }
    }

    private static FixtureRefusal Refuse(string reason) => new(FixtureRefusal.UnsupportedInvocation, reason);
}
