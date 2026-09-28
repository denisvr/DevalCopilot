using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.SetCodexAssignmentPreference;

public static class CodexAssignmentPreferenceErrors
{
    public const string RunNotFoundCode = "runs.not_found";
    public const string RunNotEditableCode = "codex_assignment.run_not_editable";
    public const string CatalogUnavailableCode = "codex_assignment.catalog_unavailable";
    public const string ModelNotVisibleCode = "codex_assignment.model_not_visible";
    public const string EffortNotSupportedCode = "codex_assignment.effort_not_supported";

    public static Error RunNotFound() => Error.NotFound(RunNotFoundCode, "This run does not exist.");

    public static Error RunNotEditable() =>
        Error.Failure(RunNotEditableCode, "This run's Codex model/effort preference can no longer be changed.");

    /// <summary>Every unavailable catalog case — no vetted Codex launch target, an unsupported
    /// protocol method, a malformed or excessive response, a timeout, or a process failure —
    /// resolves to this one closed error. A new non-null preference is never accepted without a
    /// fresh, successful observation to validate it against.</summary>
    public static Error CatalogUnavailable() =>
        Error.Failure(CatalogUnavailableCode, "The Codex model catalog is currently unavailable.");

    public static Error ModelNotVisible() =>
        Error.Failure(ModelNotVisibleCode, "The requested Codex model is not currently visible in the catalog.");

    public static Error EffortNotSupported() =>
        Error.Failure(EffortNotSupportedCode, "The requested reasoning effort is not supported by this model.");
}
