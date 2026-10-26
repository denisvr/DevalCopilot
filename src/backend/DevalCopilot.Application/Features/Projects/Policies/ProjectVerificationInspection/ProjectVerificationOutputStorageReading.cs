using DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;

namespace DevalCopilot.Application.Features.Projects.Policies.ProjectVerificationInspection;

/// <summary>The admitted descriptor of one stream and, only when it is Recorded, the stored row whose path and hash a window passes to the
/// artifact store (the caller still applies <see cref="RecordedVerificationOutputAdmission.IsAdmissibleStorage"/>).</summary>
internal sealed record ProjectVerificationOutputStorageReading(
    RecordedVerificationOutputDescriptor Descriptor, VerificationOutputStorageRow? Storage);
