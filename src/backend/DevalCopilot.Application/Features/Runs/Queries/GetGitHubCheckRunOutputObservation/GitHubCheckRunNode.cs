namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckRunOutputObservation;

/// <summary>What GitHub returned for the requested global node identifier: its GraphQL type name and, only for a check run, the
/// association and (in the output phase alone) the detail. A node of any other type carries neither.</summary>
public sealed record GitHubCheckRunNode(string TypeName, GitHubCheckRunAssociation? Association, GitHubCheckRunDetail? Detail);
