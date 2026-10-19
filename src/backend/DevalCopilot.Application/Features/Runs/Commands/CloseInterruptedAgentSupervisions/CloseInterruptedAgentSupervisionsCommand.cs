using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.CloseInterruptedAgentSupervisions;

/// <summary>
/// Startup closure of the Agent supervision observations of previous host launches (ADR-0056): after the existing attempt
/// reconciliation and before any hosted worker starts, every observation still open for another launch is closed as interrupted with its
/// first attention retained (or host_interrupted when it had none). Nothing is adopted, renewed or resumed, and the existing attempt
/// decisions are preserved: no Attempt, Run, workspace, budget or ownership changes here. Manual transaction.
/// </summary>
public sealed record CloseInterruptedAgentSupervisionsCommand
    : IManualTransactionCommand<Result<CloseInterruptedAgentSupervisionsCommandResult>>;
