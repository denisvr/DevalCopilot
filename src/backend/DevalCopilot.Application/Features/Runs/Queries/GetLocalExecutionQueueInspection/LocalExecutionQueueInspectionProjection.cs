using System.Linq.Expressions;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionQueueInspection;

/// <summary>
/// The guarded scalar projection of the global statement (ADR-0064): the candidate admission, the registered project label and, bound to
/// its own joined shape, the unchanged ADR-0063 history row. Like every inspection projection, each stored enum is classified by the
/// database, each number is an exact integer in its range and each text is bounded by its stored bytes before it is converted. Neither
/// the project path nor any other column is selected.
/// </summary>
internal static class LocalExecutionQueueInspectionProjection
{
    /// <summary>The stored bytes a project name may occupy before it is converted: at most 200 UTF-16 units of at most four bytes.</summary>
    public const int ProjectNameByteBound = 800;

    private const long Int64Maximum = long.MaxValue;

    private static readonly Expression<Func<LocalExecutionAdmission, LocalExecutionQueueInspectionCandidate>> CandidateOf =
        candidate => new LocalExecutionQueueInspectionCandidate(
            (Guid?)candidate.Id,
            (Guid?)candidate.SequenceId,
            (candidate.AdmissionNumber >= 1 && candidate.AdmissionNumber < Int64Maximum
                && candidate.AdmissionNumber % Int64Maximum == candidate.AdmissionNumber)
                || candidate.AdmissionNumber == Int64Maximum
                ? (long?)candidate.AdmissionNumber
                : null,
            candidate.State == LocalExecutionAdmissionState.Waiting ? 1
                : candidate.State == LocalExecutionAdmissionState.Reserved ? 2
                : candidate.State == LocalExecutionAdmissionState.Consumed ? 3
                : candidate.State == LocalExecutionAdmissionState.Retired ? 4
                : candidate.State == LocalExecutionAdmissionState.Unresolved ? 5
                : 0);

    private static readonly Expression<Func<Project, LocalExecutionQueueInspectionProject>> ProjectOf =
        project => new LocalExecutionQueueInspectionProject(
            (Guid?)project.Id,
            ((byte[])(object)project.Name).Length <= ProjectNameByteBound ? project.Name : null);

    /// <summary>Whether the stored state is outstanding or unrecognized: only a recognized Consumed or Retired state is excluded, so an
    /// unknown, null or malformed state stays a candidate and makes the answer Unavailable.</summary>
    public static Expression<Func<LocalExecutionAdmission, bool>> IsCandidate { get; } =
        admission => (admission.State == LocalExecutionAdmissionState.Consumed ? 3
            : admission.State == LocalExecutionAdmissionState.Retired ? 4
            : 0) == 0;

    public static Expression<Func<LocalExecutionQueueInspectionJoin, LocalExecutionQueueInspectionRow>> RowOf(
        IDevalCopilotDbContext dbContext)
    {
        var join = Expression.Parameter(typeof(LocalExecutionQueueInspectionJoin), "join");
        var constructor = typeof(LocalExecutionQueueInspectionRow).GetConstructor(
            [
                typeof(LocalExecutionQueueInspectionCandidate),
                typeof(LocalExecutionQueueInspectionProject),
                typeof(LocalExecutionAdmissionHistoryRow),
            ])!;
        var body = Expression.New(
            constructor,
            Bind(CandidateOf, Expression.Property(join, nameof(LocalExecutionQueueInspectionJoin.Candidate))),
            Bind(ProjectOf, Expression.Property(join, nameof(LocalExecutionQueueInspectionJoin.Project))),
            Bind(
                LocalExecutionAdmissionHistoryProjection.RowOf(dbContext),
                Expression.Property(join, nameof(LocalExecutionQueueInspectionJoin.History))));
        return Expression.Lambda<Func<LocalExecutionQueueInspectionJoin, LocalExecutionQueueInspectionRow>>(body, join);
    }

    /// <summary>The lambda body with its one parameter bound to <paramref name="argument"/>, so each projection stays written once.</summary>
    private static Expression Bind<TSource, TResult>(Expression<Func<TSource, TResult>> projection, Expression argument) =>
        new ParameterBinding(projection.Parameters[0], argument).Visit(projection.Body);

    private sealed class ParameterBinding(ParameterExpression parameter, Expression argument) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == parameter ? argument : base.VisitParameter(node);
    }
}
