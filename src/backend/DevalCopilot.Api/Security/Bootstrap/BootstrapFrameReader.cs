using System.Text;
using System.Text.Json;
using Devalente.Shared.Results;

namespace DevalCopilot.Api.Security.Bootstrap;

/// <summary>
/// Reads and validates exactly one bounded, versioned bootstrap frame from the Tauri
/// shell's stdin pipe. Malformed, oversized, unsupported-version, missing, or incomplete
/// input fails closed — the caller must never bind or serve when this returns a failure.
/// </summary>
public static class BootstrapFrameReader
{
    public const int SupportedVersion = 1;
    private const int SecretHexLength = 64;
    private const int MaxFrameBytes = 4096;
    private static readonly JsonSerializerOptions DeserializeOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    public static async Task<Result<BootstrapReadResult>> ReadAsync(Stream input, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxFrameBytes];
        var length = 0;
        var newlineIndex = -1;

        while (newlineIndex < 0)
        {
            if (length == buffer.Length)
            {
                return Result<BootstrapReadResult>.Failure(
                    Error.Failure("bootstrap.oversized", "The bootstrap frame exceeded the maximum allowed size."));
            }

            var read = await input.ReadAsync(buffer.AsMemory(length, buffer.Length - length), cancellationToken);
            if (read == 0)
            {
                return length == 0
                    ? Result<BootstrapReadResult>.Failure(
                        Error.Failure("bootstrap.missing", "No bootstrap frame was received before the input ended."))
                    : Result<BootstrapReadResult>.Failure(
                        Error.Failure("bootstrap.incomplete", "The bootstrap frame ended before a terminating newline."));
            }

            var searchStart = length;
            length += read;
            newlineIndex = Array.IndexOf(buffer, (byte)'\n', searchStart, length - searchStart);
        }

        // Rust writes raw UTF-8 bytes with no preamble, so a real shell never sends one. A
        // leading BOM is only ever a defensive tolerance for the rare writer that does.
        var contentStart = buffer.AsSpan(0, newlineIndex).StartsWith(Utf8Bom) ? Utf8Bom.Length : 0;
        var line = Encoding.UTF8.GetString(buffer, contentStart, newlineIndex - contentStart).TrimEnd('\r');
        var trailingStart = newlineIndex + 1;
        var trailing = buffer[trailingStart..length];

        BootstrapFrameDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<BootstrapFrameDto>(line, DeserializeOptions);
        }
        catch (JsonException)
        {
            return Result<BootstrapReadResult>.Failure(
                Error.Failure("bootstrap.malformed", "The bootstrap frame was not valid JSON."));
        }

        if (dto is null)
        {
            return Result<BootstrapReadResult>.Failure(
                Error.Failure("bootstrap.malformed", "The bootstrap frame was empty."));
        }

        if (dto.Version != SupportedVersion)
        {
            return Result<BootstrapReadResult>.Failure(
                Error.Failure("bootstrap.unsupported_version", $"Bootstrap frame version {dto.Version} is not supported."));
        }

        if (string.IsNullOrEmpty(dto.Secret) || dto.Secret.Length != SecretHexLength || !IsLowercaseHex(dto.Secret))
        {
            return Result<BootstrapReadResult>.Failure(
                Error.Failure("bootstrap.malformed", "The bootstrap frame did not contain a valid secret."));
        }

        return Result<BootstrapReadResult>.Success(new BootstrapReadResult(new BootstrapFrame(dto.Version, dto.Secret), trailing));
    }

    private static bool IsLowercaseHex(string value)
    {
        foreach (var character in value)
        {
            var isHexDigit = (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f');
            if (!isHexDigit)
            {
                return false;
            }
        }

        return true;
    }

    private sealed record BootstrapFrameDto(int Version, string? Secret);
}
