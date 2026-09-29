using System.Text.Json;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;

/// <summary>
/// Builds the bounded, versioned context-manifest content for one Codex planning attempt — the
/// smallest sufficient set of durable references for this planning stage, never a full transcript,
/// repository copy, or raw log. See <c>docs/architecture/agent-collaboration-protocol.md</c>
/// ("Token efficiency" / "Context assembly").
/// </summary>
internal static class ContextManifestBuilder
{
    /// <summary>Fixed, project-owned instruction references relevant to every planning attempt in
    /// this repository — paths relative to the repository root, never resolved or read from disk
    /// here; the manifest carries only the reference, never file content.</summary>
    private static readonly IReadOnlyList<string> InstructionReferences =
    [
        "CLAUDE.md",
        "docs/engineering-context.md",
        "docs/architecture/agent-collaboration-protocol.md",
    ];

    /// <summary>The fixed, host-authored reminder carried by a format-repair manifest. It is the
    /// only repair-specific content: never the source response, a parser detail, an artifact path,
    /// an attempt identity, or any human-supplied text. It frames the result as a fresh Proposal,
    /// not a correction of the earlier response.</summary>
    public const string FormatRepairNotice =
        "An earlier planning response for this objective failed structural validation. "
        + "This is a fresh planning request: return exactly one Proposal that satisfies the "
        + "unchanged expectedProposalSchema.";

    public static string Build(
        Guid projectId,
        Guid gitWorkspaceId,
        Guid gitCheckpointId,
        string checkpointFingerprintSha256,
        string runObjective,
        string? unresolvedHumanInstruction,
        IReadOnlyList<Guid> priorDecisionMessageIds) =>
        Serialize(
            projectId, gitWorkspaceId, gitCheckpointId, checkpointFingerprintSha256, runObjective,
            unresolvedHumanInstruction, priorDecisionMessageIds, formatRepair: false);

    /// <summary>The manifest of the one manual format-repair attempt: exactly the ordinary planning
    /// context (no human instruction and no prior decisions, as for an ordinary claim) plus
    /// <see cref="FormatRepairNotice"/>.</summary>
    public static string BuildFormatRepair(
        Guid projectId,
        Guid gitWorkspaceId,
        Guid gitCheckpointId,
        string checkpointFingerprintSha256,
        string runObjective) =>
        Serialize(
            projectId, gitWorkspaceId, gitCheckpointId, checkpointFingerprintSha256, runObjective,
            null, [], formatRepair: true);

    private static string Serialize(
        Guid projectId,
        Guid gitWorkspaceId,
        Guid gitCheckpointId,
        string checkpointFingerprintSha256,
        string runObjective,
        string? unresolvedHumanInstruction,
        IReadOnlyList<Guid> priorDecisionMessageIds,
        bool formatRepair)
    {
        // Insertion order is the serialized order; the ordinary document is byte-identical to the
        // former anonymous-type form, and the repair notice is appended only for a repair.
        var document = new Dictionary<string, object?>
        {
            ["protocolVersion"] = CollaborationMessage.ProtocolVersionOne,
            ["objective"] = runObjective,
            ["projectId"] = projectId,
            ["gitWorkspaceId"] = gitWorkspaceId,
            ["gitCheckpointId"] = gitCheckpointId,
            ["checkpointFingerprintSha256"] = checkpointFingerprintSha256,
            ["expectedMessageType"] = nameof(CollaborationMessageType.Proposal),
            ["expectedProposalSchema"] = CodexProposalOutputSchema.BuildSchemaDocument(),
            ["instructionReferences"] = InstructionReferences,
            ["unresolvedHumanInstruction"] = unresolvedHumanInstruction,
            ["priorDecisionMessageIds"] = priorDecisionMessageIds,
        };

        if (formatRepair)
        {
            document["formatRepairNotice"] = FormatRepairNotice;
        }

        return JsonSerializer.Serialize(document);
    }
}
