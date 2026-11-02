namespace DevalCopilot.Application.Features.Projects.Policies;

/// <summary>
/// ADR-0072: the bounded code an exact-shape verification reader projects from the three recorded I/O completion columns of one
/// execution. Historical executions recorded none, so <see cref="NotRecorded"/> keeps every earlier Passed and Failed shape coherent;
/// a new Passed needs <see cref="Complete"/>; and a Failed execution with an actual Exited/0 tuple is coherent only beside
/// <see cref="Incomplete"/>. A partial or unrecognized set is <see cref="Unrecognized"/> and supports neither.
/// </summary>
internal static class VerificationIoCode
{
    public const int NotRecorded = 0;
    public const int Complete = 1;
    public const int Incomplete = 2;
    public const int Unrecognized = 99;

    public static bool AllowsPassed(int code) => code is NotRecorded or Complete;

    public static bool ExplainsFailedZeroExit(int code) => code == Incomplete;
}
