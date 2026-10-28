namespace DevalCopilot.Api.Features.Runs.GetLocalCommitInspection;

/// <summary>The recorded reason of an operation (ADR-0070). <c>Kind</c> is <c>None</c> (no reason; null code), <c>Known</c> (one of the
/// finite fixed operation reason codes) or <c>Unrecognized</c> (a bounded reason this version does not describe; null code). Arbitrary
/// stored reason text is never returned.</summary>
public sealed record LocalCommitInspectionReasonResponse(string Kind, string? Code);
