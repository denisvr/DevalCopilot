using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// Bounded, sanitized evidence about a server-side failure behind an unexpected HTTP status: the first server errors the host logged
/// (type, message and the top of the stack) and the head of the response body. It only describes a failure that already happened;
/// nothing here retries, waits or changes what the tests assert.
/// </summary>
internal sealed class CapturedServerErrors : ILoggerProvider
{
    private const int MaximumEntries = 6;
    private const int MaximumEntryCharacters = 1800;
    private const int MaximumBodyCharacters = 1200;

    private readonly ConcurrentQueue<string> _entries = new();
    private readonly string[] _sensitive;
    private readonly (string Path, string Label)[] _paths;

    public CapturedServerErrors(IEnumerable<string> sensitiveValues, IEnumerable<(string Path, string Label)> paths)
    {
        _sensitive = sensitiveValues.Where(value => !string.IsNullOrEmpty(value)).ToArray();
        _paths = paths.Where(path => !string.IsNullOrEmpty(path.Path)).OrderByDescending(path => path.Path.Length).ToArray();
    }

    public ILogger CreateLogger(string categoryName) => new Capture(this, categoryName);

    public void Dispose()
    {
    }

    /// <summary>The captured server errors, oldest first, or a fixed statement when the host logged none.</summary>
    public string Describe() => _entries.IsEmpty ? "the host logged no error" : string.Join("\n---\n", _entries);

    /// <summary>The status and the sanitized, bounded head of a response whose status the test did not expect.</summary>
    public async Task<string> DescribeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return $"{(int)response.StatusCode} {response.StatusCode}; body: {Sanitize(body, MaximumBodyCharacters)}; server errors: {Describe()}";
    }

    internal string Sanitize(string text, int maximum)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            builder.Append(character is '\n' or '\t' || !char.IsControl(character) ? character : '?');
        }

        var clean = builder.ToString();
        foreach (var value in _sensitive)
        {
            clean = clean.Replace(value, "<secret>", StringComparison.Ordinal);
        }

        foreach (var (path, label) in _paths)
        {
            clean = clean.Replace(path, label, StringComparison.OrdinalIgnoreCase);
        }

        // Anything that still looks like an absolute Windows path outside the known roots is reduced to its file name.
        clean = Regex.Replace(clean, @"[A-Za-z]:\\(?:[^\\\r\n:""<>|]+\\)+", "<path>\\", RegexOptions.None, TimeSpan.FromSeconds(1));
        return clean.Length <= maximum ? clean : clean[..maximum] + $"… [{clean.Length - maximum} more characters omitted]";
    }

    private sealed class Capture(CapturedServerErrors owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel) || owner._entries.Count >= MaximumEntries)
            {
                return;
            }

            var text = $"{category} [{logLevel}] {formatter(state, exception)}" + (exception is null ? string.Empty : $"\n{exception}");
            owner._entries.Enqueue(owner.Sanitize(text, MaximumEntryCharacters));
        }
    }
}
