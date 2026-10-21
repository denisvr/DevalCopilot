using System.Text.Json;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Policies.VerificationDiagnosis;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;

/// <summary>The deterministic provider answers the process and adapter doubles return, each in the exact shape its production parser
/// accepts. They prove reachability and sealed-form agreement only, never real-provider behavior.</summary>
internal static class LocalExecutionSequenceResponses
{
    public const string FailureLine = "SENTINEL-FAILURE error CS1002: ; expected";

    public static readonly string ProposalJson = JsonSerializer.Serialize(new
    {
        scope = "Ledger",
        implementationSteps = "Add the table then the query",
        risks = "Unbounded content",
        verificationPlan = "Tests",
        escalationPoints = "None expected",
    });

    public static readonly string AcceptJson = JsonSerializer.Serialize(new
    {
        decision = "accept",
        summary = "Sound and complete.",
        rationale = "The proposal is feasible as written.",
    });

    public static readonly string ApprovedJson = JsonSerializer.Serialize(new
    {
        outcome = ImplementationReviewOutputSchema.ApprovedOutcome,
        summary = "The implementation matches the plan.",
        rationale = "Every step from the plan was followed and verification passed.",
        residualRisks = "None material.",
        findings = Array.Empty<object>(),
    });

    public static readonly string DiagnosisEscalationJson = JsonSerializer.Serialize(new
    {
        outcome = VerificationDiagnosisOutputSchema.EscalationOutcome,
        summary = "The failure needs a human decision.",
        findings = Array.Empty<object>(),
        escalation = new
        {
            unresolvedDecision = "Whether the failing check is within the scope of the plan.",
            options = "Adjust the plan scope or accept the failure as out of scope.",
            consequences = "No source change is justified until that is decided.",
            evidence = "The failure concerns local tool access outside the plan.",
            recommendedChoice = "Decide the scope before changing source.",
        },
    });

    public static string ChallengeJson(int count) => JsonSerializer.Serialize(new
    {
        decision = "challenge",
        summary = "Material issues were found.",
        challenges = Enumerable.Range(1, count).Select(index => new
        {
            summary = $"Challenge {index} raises a material concern.",
            disputedItem = $"Step {index}",
            materialImpact = "Could cause data loss",
            reasoning = "The step does not account for concurrent writers",
            alternativeOrQuestion = "Consider a serialized write path instead",
        }),
    });

    public static string ResolvedJson(IReadOnlyList<Guid> challengeIds, string scope) => JsonSerializer.Serialize(new
    {
        summary = "Every challenge has been resolved.",
        decisions = challengeIds.Select((id, index) => new
        {
            challengeMessageId = id.ToString(),
            summary = $"Decision {index + 1} accepts the challenge.",
            resolution = ChallengeResolutionOutputSchema.AcceptedResolution,
            rationale = $"Rationale {index + 1}",
            resultingPlanChanges = $"Plan changes {index + 1}",
            nextAction = $"Next action {index + 1}",
        }),
        revisedProposal = new
        {
            summary = $"Revised proposal [{scope}] addressing every challenge.",
            scope,
            implementationSteps = $"Steps unique to [{scope}]",
            risks = $"Risks unique to [{scope}]",
            verificationPlan = $"Verification unique to [{scope}]",
            escalationPoints = $"Escalation unique to [{scope}]",
        },
    });

    public static string ImplementationReport() => JsonSerializer.Serialize(new
    {
        summary = "Implemented the plan.",
        changedRelativePaths = new[] { "src/Foo.cs" },
        implementationNotes = "Added the migration and the query handler.",
        unexpectedDiscoveries = "",
        remainingRisks = "",
        recommendedVerification = "Run the backend test suite.",
    });

    public static string Correction(IReadOnlyList<Guid> findingIds) => JsonSerializer.Serialize(new
    {
        revisionResponses = findingIds.Select((findingId, index) => new
        {
            findingMessageId = findingId,
            disposition = "Fixed",
            evidence = $"The failing statement {index + 1} was completed.",
            resultingSourceChanges = $"Completed statement {index + 1}.",
        }),
        executionReport = new
        {
            summary = "Correction complete.",
            changedRelativePaths = new[] { "src/Foo.cs" },
            implementationNotes = "Applied the requested correction.",
            unexpectedDiscoveries = "None.",
            remainingRisks = "None.",
            recommendedVerification = "Run tests.",
        },
    });

    public static string DiagnosisFindings(int count) => JsonSerializer.Serialize(new
    {
        outcome = VerificationDiagnosisOutputSchema.FindingsOutcome,
        summary = "The build fails on missing statements.",
        findings = Enumerable.Range(1, count).Select(index => new
        {
            severity = ImplementationReviewOutputSchema.Severities[1],
            category = ImplementationReviewOutputSchema.Categories[0],
            summary = $"Statement {index} is incomplete.",
            evidence = $"The build output reports a syntax error at statement {index}.",
            requiredChange = $"Complete statement {index}.",
            affectedRelativePath = (string?)"src/Foo.cs",
        }),
        escalation = (object?)null,
    });

    public static string ChangesRequested(int count) => JsonSerializer.Serialize(new
    {
        outcome = ImplementationReviewOutputSchema.ChangesRequestedOutcome,
        summary = "Material issues were found.",
        rationale = (string?)null,
        residualRisks = (string?)null,
        findings = Enumerable.Range(1, count).Select(index => new
        {
            severity = ImplementationReviewOutputSchema.Severities[0],
            category = ImplementationReviewOutputSchema.Categories[0],
            summary = $"The branch {index} is incomplete.",
            evidence = $"Evidence {index}",
            requiredChange = $"Complete branch {index}.",
            affectedRelativePath = (string?)"src/Foo.cs",
        }),
    });
}
