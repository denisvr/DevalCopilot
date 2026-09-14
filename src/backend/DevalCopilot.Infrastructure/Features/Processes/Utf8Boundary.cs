namespace DevalCopilot.Infrastructure.Features.Processes;

/// <summary>
/// Finds where a byte buffer's trailing UTF-8 sequence is genuinely incomplete — shared by every
/// place in this feature that must never treat a valid multi-byte codepoint split across a
/// boundary (a redaction flush boundary, or a bounded cursor read) as if it were malformed. A
/// boundary that lands mid-codepoint must defer those trailing bytes rather than decode them
/// prematurely (which would replacement-char them, permanently, in a result that has already
/// been handed to a caller) or split them across two separate decode operations.
/// </summary>
internal static class Utf8Boundary
{
    /// <summary>
    /// Returns how many leading bytes of <paramref name="buffer"/> form only complete UTF-8
    /// sequences — walking back from the end past continuation bytes to the sequence's lead
    /// byte, then checking whether the buffer actually holds enough bytes to complete it. A lead
    /// byte that does not match any recognized multi-byte pattern is treated as its own complete
    /// one-byte unit (already-malformed input is not held back forever waiting for bytes that
    /// would never complete it).
    /// </summary>
    public static int FindDecodableLength(ReadOnlySpan<byte> buffer)
    {
        var length = buffer.Length;
        var index = length - 1;
        var continuationBytesSeen = 0;

        while (index >= 0 && continuationBytesSeen < 3 && (buffer[index] & 0b1100_0000) == 0b1000_0000)
        {
            index--;
            continuationBytesSeen++;
        }

        if (index < 0)
        {
            return length;
        }

        var leadByte = buffer[index];
        var expectedSequenceLength =
            (leadByte & 0b1000_0000) == 0b0000_0000 ? 1 :
            (leadByte & 0b1110_0000) == 0b1100_0000 ? 2 :
            (leadByte & 0b1111_0000) == 0b1110_0000 ? 3 :
            (leadByte & 0b1111_1000) == 0b1111_0000 ? 4 : 1;

        var bytesAvailableFromLead = length - index;
        return bytesAvailableFromLead >= expectedSequenceLength ? length : index;
    }
}
