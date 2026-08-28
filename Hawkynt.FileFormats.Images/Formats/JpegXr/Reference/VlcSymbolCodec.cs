namespace SharpAstro.Jxr;

/// <summary>
/// Encodes/decodes a single adaptive-VLC symbol using the <see cref="VlcTables"/>.
/// The active table index comes from <see cref="AdaptiveHuffman"/>. Sign bits and
/// run/level structure are handled by the higher-level coefficient coders; this is
/// just the raw Huffman symbol.
/// </summary>
internal static class VlcSymbolCodec
{
    /// <summary>
    /// Write the VLC code for <paramref name="symbol"/> (jxrlib
    /// <c>putBit16z(m_pTable[idx*2+1], m_pTable[idx*2+2])</c>): the code value over
    /// its bit length, MSB-first.
    /// </summary>
    public static void Encode(BitWriter w, int nSym, int tableIndex, int symbol)
    {
        var (code, length) = VlcTables.GetCode(nSym, tableIndex, symbol);
        w.WriteBits((uint)code, length);
    }

    /// <summary>
    /// Decode one symbol from <paramref name="decodeTable"/> (jxrlib <c>getHuff</c>):
    /// peek 5 root bits; a non-negative entry yields <c>symbol = entry &gt;&gt; 3</c>
    /// consuming <c>entry &amp; 7</c> bits; a negative entry escapes into the
    /// extension nodes (<c>entry + 32768</c>), reading one bit per level until the
    /// table yields a non-negative symbol.
    /// </summary>
    public static int Decode(short[] decodeTable, ref BitReader r)
    {
        int sym = decodeTable[r.PeekBits(HuffmanRootBits)];
        r.SkipBits(sym < 0 ? HuffmanRootBits : sym & ((1 << HuffmanRootBitsLog) - 1));
        int symHuff = sym >> HuffmanRootBitsLog;

        if (symHuff < 0)
        {
            symHuff = sym;
            do
            {
                symHuff = decodeTable[symHuff + ExtensionBias + (int)r.ReadBits(1)];
            }
            while (symHuff < 0);
        }
        return symHuff;
    }

    // decode.h: HUFFMAN_DECODE_ROOT_BITS = 5, HUFFMAN_DECODE_ROOT_BITS_LOG = 3.
    private const int HuffmanRootBits = 5;
    private const int HuffmanRootBitsLog = 3;
    // SIGN_BIT(short) = 1 << 15 — turns a negative escape entry into its extension offset.
    private const int ExtensionBias = 1 << 15;
}
