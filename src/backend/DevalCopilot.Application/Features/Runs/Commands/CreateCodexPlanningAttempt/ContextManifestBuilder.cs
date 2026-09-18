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

    public static string Build(
        Guid projectId,
        Guid gitWorkspaceId,
        Guid gitCheckpointId,
        string checkpointFingerprintSha256,
        string runObjective,
        string? unresolvedHumanInstruction,
        IReadOnlyList<Guid> priorDecisionMessageIds)
    {
        var document = new
        {
            protocolVersion = CollaborationMessage.ProtocolVersionOne,
            objective = runObjective,
            projectId,
            gitWorkspaceId,
            gitCheckpointId,
            checkpointFingerprintSha256,
            expectedMessageType = nameof(CollaborationMessageType.Proposal),
            expectedProposalSchema = CodexProposalOutputSchema.BuildSchemaDocument(),
            instructionReferences = InstructionReferences,
            unresolvedHumanInstruction,
            priorDecisionMessageIds,
        };

        return JsonSerializer.Serialize(document);
    }
}
