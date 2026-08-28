namespace SharpAstro.Jxr;

/// <summary>
/// DECODE_QP_INDEX / encoder counterpart — T.832 §8.7.10.10 / Table 47.
/// Per-MB index that picks a row of an <see cref="QpTable"/> when the band
/// is non-uniform (NumLPQPs > 1 or NumHPQPs > 1). The bitstream is a 1-bit
/// nonzero flag followed by an n-bit fixed-width index (n derived from the
/// table size via the iBitsQPIndex LUT).
/// </summary>
internal static class QpIndex
{
    // T.832 Table 47 iBitsQPIndex[]. Indexed by NumQPs (1..16). For NumQPs=1
    // the function isn't called (no QP_INDEX bit emitted); for NumQPs=2 we
    // emit 1 extra bit (after the nonzero flag), etc. — value at index 16
    // is the max 4 bits.
    private static ReadOnlySpan<byte> BitsTable =>
        [0, 0, 1, 1, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4, 4];

    /// <summary>
    /// Encoder: write the QP_INDEX for a single MB given the chosen row
    /// index in <c>0..numQPs-1</c>.
    /// </summary>
    internal static void Write(BitWriter writer, int numQPs, int qpIndex)
    {
        if (numQPs <= 1) return; // No bit emitted.
        if ((uint)qpIndex >= (uint)numQPs)
            throw new ArgumentOutOfRangeException(nameof(qpIndex), qpIndex,
                $"qpIndex {qpIndex} out of range [0, {numQPs})");

        if (qpIndex == 0)
        {
            writer.WriteBit(false);              // IS_QPINDEX_NONZERO_FLAG
            return;
        }
        writer.WriteBit(true);
        int iBits = BitsTable[numQPs];
        writer.WriteBits((uint)(qpIndex - 1), iBits);
    }

    /// <summary>Decoder: read one QP_INDEX. Returns 0 when numQPs == 1 (no bit consumed).</summary>
    internal static int Read(ref BitReader reader, int numQPs)
    {
        if (numQPs <= 1) return 0;
        if (!reader.ReadBit()) return 0;
        int iBits = BitsTable[numQPs];
        return (int)reader.ReadBits(iBits) + 1;
    }
}
