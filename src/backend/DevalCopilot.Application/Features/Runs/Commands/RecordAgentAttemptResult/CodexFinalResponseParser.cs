using System.Text.Json;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptResult;

/// <summary>
/// Validates a Codex final response against the exact protocol-owned Proposal shape
/// (<see cref="CodexProposalOutputSchema"/>) — never trusting that the provider actually honored
/// the schema it was given. Unknown fields, a non-object root, a non-string field value, a blank
/// value, a missing required field, malformed JSON, or content that would fail
/// <see cref="CollaborationMessage.Record"/>'s own <see cref="CollaborationMessageContentPolicy"/>
/// checks (excessive length, unsafe-looking content) all fail closed to <see langword="null"/>;
/// none of them ever partially populate a result. Applying that same policy here — rather than
/// merely trusting it will be applied later — is what makes this genuinely defense in depth: a
/// policy-violating result is reported as <c>AgentOutcome.InvalidStructuredOutput</c> by the
/// caller, never left to surface as an unhandled exception out of <c>CollaborationMessage.Record</c>.
/// </summary>
public static class CodexFinalResponseParser
{
    public static ValidatedProposal? TryParse(string finalResponseJson)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(finalResponseJson);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var seenFields = new HashSet<string>(StringComparer.Ordinal);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var property in root.EnumerateObject())
            {
                if (!CodexProposalOutputSchema.RequiredFields.Contains(property.Name) || !seenFields.Add(property.Name))
                {
                    return null;
                }

                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                var value = property.Value.GetString();
                if (string.IsNullOrWhiteSpace(value))
                {
                    return null;
                }

                values[property.Name] = value;
            }

            if (seenFields.Count != CodexProposalOutputSchema.RequiredFields.Count)
            {
                return null;
            }

            var summary = values["summary"];
            if (!CollaborationMessageContentPolicy.IsSafeSummary(summary))
            {
                return null;
            }

            var structuredContentJson = JsonSerializer.Serialize(new
            {
                scope = values["scope"],
                implementationSteps = values["implementationSteps"],
                risks = values["risks"],
                verificationPlan = values["verificationPlan"],
                escalationPoints = values["escalationPoints"],
            });

            try
            {
                CollaborationMessageContentPolicy.Validate(CollaborationMessageType.Proposal, structuredContentJson);
            }
            catch (ArgumentException)
            {
                return null;
            }

            return new ValidatedProposal(summary, structuredContentJson);
        }
    }
}
