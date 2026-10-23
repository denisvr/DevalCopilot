namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionQueueInspection;

/// <summary>One cloned queued cycle of a bounded fixture: its own project, run, workspace, checkpoint, sequence and admission.</summary>
internal sealed record ClonedCycle(Guid ProjectId, Guid RunId, Guid SequenceId, Guid AdmissionId, string ProjectName);
