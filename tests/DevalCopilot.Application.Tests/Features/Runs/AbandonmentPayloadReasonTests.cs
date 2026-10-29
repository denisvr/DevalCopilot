using DevalCopilot.Application.Features.Runs.Policies.Abandonment;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>ADR-0031: the abandonment event payload is <c>{"reason": text}</c> and anything else is no reason at all, so an unreadable
/// payload makes the abandonment incoherent instead of failing a read. A JSON string whose escapes are not a well-formed UTF-16 text (an
/// isolated, misordered or unmatched surrogate escape) is valid JSON but not text, so it is no reason either and never throws. Escapes
/// are assembled from a backslash character so no tool can turn the damaged case into a valid one before the reader sees it.</summary>
public sealed class AbandonmentPayloadReasonTests
{
    private const char Backslash = (char)92;

    private static string Payload(string escapedText) => "{\"reason\":\"" + escapedText + "\"}";

    private static string Escape(string hex) => Backslash + "u" + hex;

    private static string Scalar(int scalar) => char.ConvertFromUtf32(scalar);

    public static TheoryData<string> UnreadableStrings() =>
    [
        "isolated high surrogate",
        "isolated low surrogate",
        "lowest high surrogate in upper case digits",
        "highest high surrogate",
        "highest low surrogate",
        "high surrogate inside text",
        "high surrogate at the end of text",
        "low surrogate at the start of text",
        "high surrogate followed by a high surrogate",
        "low surrogate followed by a high surrogate",
        "high surrogate followed by a plain escape",
        "high surrogate followed by a plain character",
        "high surrogate followed by a short escape",
        "low surrogate after a valid pair",
        "high surrogate after a valid pair",
        "high surrogate before a valid pair",
    ];

    private static string UnreadablePayload(string name) => Payload(name switch
    {
        "isolated high surrogate" => Escape("d800"),
        "isolated low surrogate" => Escape("dc00"),
        "lowest high surrogate in upper case digits" => Escape("D800"),
        "highest high surrogate" => Escape("dbff"),
        "highest low surrogate" => Escape("dfff"),
        "high surrogate inside text" => "a" + Escape("d800") + "b",
        "high surrogate at the end of text" => "text" + Escape("d83d"),
        "low surrogate at the start of text" => Escape("de00") + "text",
        "high surrogate followed by a high surrogate" => Escape("d800") + Escape("d800"),
        "low surrogate followed by a high surrogate" => Escape("dc00") + Escape("d800"),
        "high surrogate followed by a plain escape" => Escape("d800") + Escape("0041"),
        "high surrogate followed by a plain character" => Escape("d800") + "A",
        "high surrogate followed by a short escape" => Escape("d800") + Backslash + "n",
        "low surrogate after a valid pair" => Escape("d834") + Escape("dd1e") + Escape("dc00"),
        "high surrogate after a valid pair" => Escape("d834") + Escape("dd1e") + Escape("d800"),
        "high surrogate before a valid pair" => Escape("d800") + Escape("d834") + Escape("dd1e"),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    });

    [Theory]
    [MemberData(nameof(UnreadableStrings))]
    public void An_escaped_surrogate_that_is_not_part_of_a_valid_pair_is_no_reason_and_never_throws(string name)
    {
        Assert.Null(RunAbandonmentReader.ReadReason(UnreadablePayload(name)));
    }

    [Fact]
    public void An_unreadable_reason_is_no_reason_whatever_else_the_object_holds()
    {
        var damaged = Payload(Escape("d800"));

        Assert.Null(RunAbandonmentReader.ReadReason(damaged.Replace("{", "{\"note\":\"x\",", StringComparison.Ordinal)));
        Assert.Null(RunAbandonmentReader.ReadReason(damaged.Replace("}", ",\"note\":\"x\"}", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_damaged_string_elsewhere_in_the_object_is_never_decoded_and_does_not_hide_a_readable_reason()
    {
        var note = Escape("d800");

        Assert.Equal("ok", RunAbandonmentReader.ReadReason("{\"note\":\"" + note + "\",\"reason\":\"ok\"}"));
        Assert.Equal("ok", RunAbandonmentReader.ReadReason("{\"reason\":\"ok\",\"note\":\"" + note + "\"}"));
        Assert.Equal("ok", RunAbandonmentReader.ReadReason("{\"" + note + "\":1,\"reason\":\"ok\"}"));
    }

    public static TheoryData<string> ReadableStrings() =>
    [
        "an ordinary reason",
        "an empty reason",
        "escaped basic characters",
        "an escaped valid pair",
        "an escaped valid pair inside text",
        "two escaped valid pairs",
        "the lowest and the highest valid pairs",
        "a literal supplementary character",
        "a literal supplementary character beside an escaped pair",
        "an escaped character beside a literal one",
    ];

    private static (string Payload, string Expected) ReadablePayload(string name) => name switch
    {
        "an ordinary reason" => (Payload("The objective was replaced by a smaller change."), "The objective was replaced by a smaller change."),
        "an empty reason" => (Payload(string.Empty), string.Empty),
        "escaped basic characters" => (Payload(Escape("00e9") + Escape("0041") + Backslash + "n" + Backslash + Backslash), Scalar(0xE9) + "A\n" + Backslash),
        "an escaped valid pair" => (Payload(Escape("d834") + Escape("dd1e")), Scalar(0x1D11E)),
        "an escaped valid pair inside text" => (
            Payload("a " + Escape("d83d") + Escape("de00") + " b"), "a " + Scalar(0x1F600) + " b"),
        "two escaped valid pairs" => (Payload(Escape("d834") + Escape("dd1e") + Escape("d83d") + Escape("de00")), Scalar(0x1D11E) + Scalar(0x1F600)),
        "the lowest and the highest valid pairs" => (
            Payload(Escape("d800") + Escape("dc00") + Escape("dbff") + Escape("dfff")), Scalar(0x10000) + Scalar(0x10FFFF)),
        "a literal supplementary character" => (Payload(Scalar(0x1D11E)), Scalar(0x1D11E)),
        "a literal supplementary character beside an escaped pair" => (
            Payload(Scalar(0x1D11E) + Escape("d83d") + Escape("de00")), Scalar(0x1D11E) + Scalar(0x1F600)),
        "an escaped character beside a literal one" => (Payload(Escape("00e9") + Scalar(0xE9)), Scalar(0xE9) + Scalar(0xE9)),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [MemberData(nameof(ReadableStrings))]
    public void A_well_formed_reason_is_read_exactly_as_before(string name)
    {
        var (payload, expected) = ReadablePayload(name);

        Assert.Equal(expected, RunAbandonmentReader.ReadReason(payload));
    }

    public static TheoryData<string> NotAnAbandonmentPayload() =>
    [
        string.Empty,
        "   ",
        "{",
        "not json",
        "{\"reason\":",
        "{\"reason\":\"unterminated}",
        "[]",
        "[\"reason\"]",
        "\"reason\"",
        "7",
        "null",
        "true",
        "{}",
        "{\"Reason\":\"x\"}",
        "{\"reason\":null}",
        "{\"reason\":7}",
        "{\"reason\":true}",
        "{\"reason\":[\"x\"]}",
        "{\"reason\":{\"text\":\"x\"}}",
    ];

    [Theory]
    [MemberData(nameof(NotAnAbandonmentPayload))]
    public void Malformed_or_non_string_payloads_stay_no_reason(string payload)
    {
        Assert.Null(RunAbandonmentReader.ReadReason(payload));
    }
}
