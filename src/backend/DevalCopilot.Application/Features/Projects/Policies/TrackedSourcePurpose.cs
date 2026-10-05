namespace DevalCopilot.Application.Features.Projects.Policies;

/// <summary>
/// Why attested tracked sources are compared. The purpose is always stated by the caller and has no default, so a new consumer can
/// never inherit another purpose's policy by omission. It selects only how the two root instruction names are treated; every other
/// proof, bound and omission rule is common to both purposes.
/// </summary>
public enum TrackedSourcePurpose
{
    /// <summary>New Agent context (ADR-0021, ADR-0024): <c>AGENTS.md</c> and <c>CLAUDE.md</c> are reserved to the controlled
    /// instruction section, so their tracked text is never delivered and never even read for tracked evidence.</summary>
    AgentDelivery,

    /// <summary>A human inspecting one checkpoint (ADR-0027): a physically proven tracked root instruction file is inert
    /// displayed source text like any other tracked file. Nothing is imported from it and it grants no authority.</summary>
    HumanInspection,
}
