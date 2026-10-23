using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionQueueInspection;

/// <summary>One proved outstanding admission: the registered project's descriptive label, the run, the sequence header and the admission,
/// which is the proved last admission of that sequence. A project name is untrusted descriptive text, never pinned authority.</summary>
public sealed record LocalExecutionQueueInspectionEntry(
    Guid ProjectId,
    string ProjectName,
    Guid RunId,
    LocalExecutionAdmissionHistoryHeader Sequence,
    LocalExecutionInspectionAdmission Admission);
