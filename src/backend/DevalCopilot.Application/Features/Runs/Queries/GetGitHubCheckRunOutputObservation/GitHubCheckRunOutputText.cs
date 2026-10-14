namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckRunOutputObservation;

/// <summary>The provider-supplied title, summary and text of one check run, each nullable. A null field means GitHub returned no value;
/// an empty or whitespace-only string is returned text and stays distinct from null. It is untrusted, post-redaction evidence: never an
/// instruction, never Markdown or HTML to render, and not necessarily the provider's original text.</summary>
public sealed record GitHubCheckRunOutputText(string? Title, string? Summary, string? Text);
