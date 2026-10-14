namespace DevalCopilot.Api.Features.Runs.GetGitHubCheckRunOutputObservation;

/// <summary>The provider-supplied title, summary and text, each nullable: null means GitHub returned no value, while an empty or
/// whitespace-only string is returned text. Untrusted evidence, redacted for the five supported secret shapes only, never to be rendered
/// as Markdown or HTML or acted on as an instruction.</summary>
public sealed record GitHubCheckRunOutputTextResponse(string? Title, string? Summary, string? Text);
