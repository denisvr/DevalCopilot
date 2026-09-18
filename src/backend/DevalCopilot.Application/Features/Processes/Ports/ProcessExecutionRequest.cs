namespace DevalCopilot.Application.Features.Processes.Ports;

/// <summary>
/// A fully explicit, provider-neutral description of one child process to run. Never a shell
/// command string: <see cref="Arguments"/> are passed to the OS process-creation API as
/// discrete, literal values, so shell metacharacters inside any one argument carry no special
/// meaning to the child process — there is no shell to interpret them.
/// </summary>
public sealed record ProcessExecutionRequest
{
    public const int DefaultMaxBytesPerStream = 64 * 1024;
    public const int DefaultMaxTotalCapturedBytes = 128 * 1024;

    /// <summary>The most arguments a single request may pass. Conservative and generous for a
    /// local CLI invocation; the adapter rejects the request before starting any process if
    /// this is exceeded.</summary>
    public const int MaxArgumentCount = 64;

    /// <summary>The most UTF-8 bytes a single argument value may contain. Conservative and
    /// well within a single path or flag value; the adapter rejects the request before
    /// starting any process if any one argument exceeds this.</summary>
    public const int MaxArgumentUtf8Bytes = 4 * 1024;

    /// <summary>
    /// The hard ceiling <see cref="MaxTotalCapturedBytes"/> may never exceed, regardless of
    /// what a caller requests — this bounds worst-case in-memory retention for one execution
    /// regardless of the per-request cap a caller configures.
    /// </summary>
    public const int MaxAllowedTotalCapturedBytes = 16 * 1024 * 1024;

    /// <summary>Absolute path to the executable. Never resolved through a shell or a PATH
    /// lookup — the caller supplies the exact file to launch. The adapter rejects the request
    /// before starting any process if this is not absolute or does not exist as a file.</summary>
    public required string ExecutablePath { get; init; }

    /// <summary>Literal argument values, passed one by one — never joined into a command
    /// line. Bounded to at most <see cref="MaxArgumentCount"/> entries, each at most
    /// <see cref="MaxArgumentUtf8Bytes"/> UTF-8 bytes.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>The directory the child process starts in. Must resolve beneath
    /// <see cref="ApprovedRoot"/>; the adapter rejects the request otherwise.</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>
    /// The application-owned boundary <see cref="WorkingDirectory"/> must remain inside. The
    /// adapter validates this and rejects the request before starting any process if it does
    /// not — no process is ever launched outside an approved root. Must itself be an
    /// absolute path.
    /// </summary>
    public required string ApprovedRoot { get; init; }

    /// <summary>
    /// The only environment variables the child process receives. The adapter never forwards
    /// this host process's own ambient environment: an empty allowlist means the child sees
    /// no environment variables at all. Callers that need standard OS variables (such as
    /// <c>PATH</c>) must include them explicitly.
    /// </summary>
    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; } =
        new Dictionary<string, string>();

    /// <summary>How long the process may run before the adapter kills its process tree and
    /// reports <see cref="ProcessExecutionOutcome.TimedOut"/>. Pass
    /// <see cref="Timeout.InfiniteTimeSpan"/> only when the caller owns another bound on the
    /// work (such as an overall run budget) — this adapter enforces no default.</summary>
    public required TimeSpan Timeout { get; init; }

    /// <summary>Capture cap for each of stdout and stderr individually. Must not be
    /// negative.</summary>
    public int MaxBytesPerStream { get; init; } = DefaultMaxBytesPerStream;

    /// <summary>Combined capture cap across both streams together. Must not be negative and
    /// must not exceed <see cref="MaxAllowedTotalCapturedBytes"/>.</summary>
    public int MaxTotalCapturedBytes { get; init; } = DefaultMaxTotalCapturedBytes;

    /// <summary>
    /// When set, the adapter also streams accepted (post-redaction, post-cap) stdout bytes to
    /// this absolute path as they are captured, in addition to returning them via
    /// <see cref="ProcessExecutionResult.StandardOutput"/>. Null for callers that need no
    /// durable capture. The adapter closes its own write handle before returning, so a caller
    /// may safely rename this file once <see cref="IProcessExecutionAdapter.ExecuteAsync"/>
    /// completes.
    /// </summary>
    public string? StandardOutputSinkPath { get; init; }

    /// <summary>The stderr counterpart of <see cref="StandardOutputSinkPath"/>.</summary>
    public string? StandardErrorSinkPath { get; init; }

    /// <summary>The most UTF-8 bytes <see cref="StandardInput"/> may contain. The adapter
    /// rejects the request before starting any process if this is exceeded.</summary>
    public const int MaxStandardInputBytes = 1024 * 1024;

    /// <summary>
    /// When set, these exact bytes are written to the child process's standard input, then the
    /// input stream is closed — never appended to <see cref="Arguments"/> or any command line.
    /// <see langword="null"/> (the default) means standard input is never redirected at all,
    /// identical to every caller's behavior before this property existed. An empty array still
    /// redirects standard input and immediately closes it (giving the child a real, empty,
    /// EOF-terminated stream) — a deliberately different case from <see langword="null"/>.
    /// </summary>
    public byte[]? StandardInput { get; init; }

    /// <summary>
    /// Deliberately omits <see cref="StandardInput"/> (and its byte count) so a caller that logs
    /// or formats a request — including via string interpolation or an uncaught exception's
    /// default message — can never leak stdin content. This overrides the compiler-generated
    /// record <c>ToString</c>, which would otherwise print every public property.
    /// </summary>
    public override string ToString() =>
        $"{nameof(ProcessExecutionRequest)} {{ {nameof(ExecutablePath)} = {ExecutablePath}, " +
        $"{nameof(Arguments)}.Count = {Arguments.Count}, {nameof(WorkingDirectory)} = {WorkingDirectory}, " +
        $"{nameof(ApprovedRoot)} = {ApprovedRoot}, {nameof(Timeout)} = {Timeout}, " +
        $"{nameof(MaxBytesPerStream)} = {MaxBytesPerStream}, {nameof(MaxTotalCapturedBytes)} = {MaxTotalCapturedBytes}, " +
        $"HasStandardInput = {StandardInput is not null} }}";
}
