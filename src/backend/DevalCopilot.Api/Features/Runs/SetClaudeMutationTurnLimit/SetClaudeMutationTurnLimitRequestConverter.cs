using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevalCopilot.Api.Features.Runs.SetClaudeMutationTurnLimit;

/// <summary>
/// Reads the one-member request strictly so a limit is never guessed from ambiguous input. The
/// <c>maxTurns</c> member (case-insensitive, like the rest of the API) must appear exactly once and be either
/// <c>null</c> or a JSON integer token that fits an <see cref="int"/>. A duplicate member, a missing member, a
/// number read from a string, a fraction (including <c>5.0</c>), an exponent, or any other token type is a
/// <see cref="JsonException"/>, which the MVC model binder reports as HTTP 400 before any handler runs. Unknown
/// members are skipped, as elsewhere in the API. The 1..100 range is enforced afterwards by the validator.
/// </summary>
public sealed class SetClaudeMutationTurnLimitRequestConverter : JsonConverter<SetClaudeMutationTurnLimitRequest>
{
    private const string MemberName = "maxTurns";

    public override SetClaudeMutationTurnLimitRequest Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("The request must be a JSON object.");
        }

        var seen = false;
        int? maxTurns = null;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return seen
                    ? new SetClaudeMutationTurnLimitRequest { MaxTurns = maxTurns }
                    : throw new JsonException("The maxTurns member is required.");
            }

            var isMember = string.Equals(reader.GetString(), MemberName, StringComparison.OrdinalIgnoreCase);
            reader.Read();
            if (!isMember)
            {
                reader.Skip();
                continue;
            }

            if (seen)
            {
                throw new JsonException("The maxTurns member must appear only once.");
            }

            seen = true;
            maxTurns = reader.TokenType switch
            {
                JsonTokenType.Null => null,
                JsonTokenType.Number when reader.TryGetInt32(out var value) => value,
                _ => throw new JsonException("The maxTurns member must be null or a whole number."),
            };
        }

        throw new JsonException("The request ended unexpectedly.");
    }

    public override void Write(Utf8JsonWriter writer, SetClaudeMutationTurnLimitRequest value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.MaxTurns is { } maxTurns)
        {
            writer.WriteNumber(MemberName, maxTurns);
        }
        else
        {
            writer.WriteNull(MemberName);
        }

        writer.WriteEndObject();
    }
}
