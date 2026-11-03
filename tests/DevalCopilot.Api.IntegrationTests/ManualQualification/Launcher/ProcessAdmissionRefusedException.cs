namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>A listed or unlisted process request was refused before its process was created. Only the closed code travels.</summary>
public sealed class ProcessAdmissionRefusedException(string code) : InvalidOperationException(code)
{
    public string Code { get; } = code;
}
