using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>
/// Decides the result each claimed step of a driven cycle records. The default is the zero-correction path (implementation Implemented,
/// every recipe passes, review approves). Overrides are by exact (kind) or (kind, pass) or (pass, recipe) so a test names exactly the
/// permutation it studies; <see cref="Findings"/> sets how many findings a review or diagnosis result records.
/// </summary>
public sealed class LocalExecutionStepScript
{
    private readonly Dictionary<(LocalExecutionStepKind Kind, int Pass), ScriptedResultKind> byPass = [];
    private readonly Dictionary<(int Pass, int Recipe), ScriptedResultKind> byRecipe = [];
    private readonly Dictionary<LocalExecutionStepKind, ScriptedResultKind> byKind = [];

    /// <summary>How many findings a ChangesRequested review or a findings diagnosis records.</summary>
    public int Findings { get; set; } = 1;

    /// <summary>The kind overriding every step of <paramref name="kind"/>.</summary>
    public LocalExecutionStepScript On(LocalExecutionStepKind kind, ScriptedResultKind result)
    {
        byKind[kind] = result;
        return this;
    }

    /// <summary>The result of the step of <paramref name="kind"/> in verification pass <paramref name="pass"/>.</summary>
    public LocalExecutionStepScript On(LocalExecutionStepKind kind, int pass, ScriptedResultKind result)
    {
        byPass[(kind, pass)] = result;
        return this;
    }

    /// <summary>The result of recipe <paramref name="recipe"/> in pass <paramref name="pass"/>.</summary>
    public LocalExecutionStepScript Verify(int pass, int recipe, ScriptedResultKind result)
    {
        byRecipe[(pass, recipe)] = result;
        return this;
    }

    public ScriptedResultKind Decide(LocalExecutionStepContext step)
    {
        if (step.Kind == LocalExecutionStepKind.Verification && byRecipe.TryGetValue((step.PassNumber, step.RecipeOrdinal!.Value), out var recipe))
        {
            return recipe;
        }

        if (byPass.TryGetValue((step.Kind, step.PassNumber), out var pass))
        {
            return pass;
        }

        if (byKind.TryGetValue(step.Kind, out var kind))
        {
            return kind;
        }

        return step.Kind switch
        {
            LocalExecutionStepKind.Implementation => ScriptedResultKind.Implemented,
            LocalExecutionStepKind.Verification => ScriptedResultKind.VerificationPassed,
            LocalExecutionStepKind.CodeReview => ScriptedResultKind.ReviewApproved,
            LocalExecutionStepKind.VerificationDiagnosis => ScriptedResultKind.DiagnosisFindings,
            _ => ScriptedResultKind.CorrectionApplied,
        };
    }
}
