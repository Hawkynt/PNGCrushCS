namespace SharpAstro.Jxr;

/// <summary>
/// The static adaptive-VLC tables from jxrlib (image/sys/adapthuff.c), transcribed
/// verbatim. For each alphabet size (4, 5, 6, 7, 8, 9, 12) there are one or more
/// VLC tables; <see cref="AdaptiveHuffman"/> picks the active one by index.
/// </summary>
/// <remarks>
/// Layout notes (matching jxrlib exactly):
/// <list type="bullet">
/// <item><b>Code tables</b> (<c>g*CodeTable</c>): each table block is
/// <c>nSym*2 + 1</c> ints — a leading alphabet-size marker, then a
/// <c>(code, length)</c> pair per symbol. So for symbol <c>s</c> in block <c>t</c>:
/// code = <c>table[(nSym*2+1)*t + s*2 + 1]</c>, length = <c>… + 2</c>.</item>
/// <item><b>Length tables</b> (<c>g_Index*Table</c>): the per-symbol code lengths,
/// used here only to cross-check the code-table length column.</item>
/// <item><b>Decode tables</b> (<c>g*HuffLookupTable</c>): peek 5 bits
/// (HUFFMAN_DECODE_ROOT_BITS) to index; a non-negative entry packs
/// <c>(symbol &lt;&lt; 3) | length</c> (ROOT_BITS_LOG = 3); a negative entry is a
/// two-level escape — <c>entry + 32768</c> is the offset into the extension nodes.</item>
/// </list>
/// </remarks>
internal static class VlcTables
{
    // ---------------- alphabet 4 (1 table) ----------------
    public static readonly int[] Code4 = { 4, 1, 1, 1, 2, 0, 3, 1, 3 };
    public static readonly int[] Len4 = { 1, 2, 3, 3 };
    public static readonly short[][] Dec4 =
    {
        new short[] { 19,19,19,19,27,27,27,27,10,10,10,10,10,10,10,10, 1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1, 0,0,0,0,0,0,0,0 },
    };

    // ---------------- alphabet 5 (2 tables) ----------------
    public static readonly int[] Code5 =
    {
        5, 1,1, 1,2, 1,3, 0,4, 1,4,
        5, 1,1, 0,3, 1,3, 2,3, 3,3,
    };
    public static readonly int[] Len5 = { 1,2,3,4,4,  1,3,3,3,3 };
    public static readonly int[] Delta5 = { 0, -1, 0, 1, 1 };
    public static readonly short[][] Dec5 =
    {
        new short[] { 28,28,36,36,19,19,19,19,10,10,10,10,10,10,10,10, 1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1, 0,0,0,0,0,0,0,0,0,0 },
        new short[] { 11,11,11,11,19,19,19,19,27,27,27,27,35,35,35,35, 1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1, 0,0,0,0,0,0,0,0,0,0 },
    };

    // ---------------- alphabet 6 (4 tables, dual-discriminant) ----------------
    public static readonly int[] Code6 =
    {
        6, 1,1, 0,5, 1,3, 1,5, 1,2, 1,4,
        6, 1,2, 0,4, 2,2, 1,4, 3,2, 1,3,
        6, 0,4, 1,4, 1,2, 2,2, 3,2, 1,3,
        6, 0,5, 1,5, 1,2, 1,1, 1,4, 1,3,
    };
    public static readonly int[] Len6 = { 1,5,3,5,2,4,  2,4,2,4,2,3,  4,4,2,2,2,3,  5,5,2,1,4,3 };
    public static readonly int[] Delta6 = { -1,1,1,1,0,1,  -2,0,0,2,0,0,  -1,-1,0,1,-2,0 };
    public static readonly short[][] Dec6 =
    {
        new short[] { 13,29,44,44,19,19,19,19,34,34,34,34,34,34,34,34, 1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1, 0,0,0,0,0,0,0,0,0,0,0,0 },
        new short[] { 12,12,28,28,43,43,43,43,2,2,2,2,2,2,2,2, 18,18,18,18,18,18,18,18,34,34,34,34,34,34,34,34, 0,0,0,0,0,0,0,0,0,0,0,0 },
        new short[] { 4,4,12,12,43,43,43,43,18,18,18,18,18,18,18,18, 26,26,26,26,26,26,26,26,34,34,34,34,34,34,34,34, 0,0,0,0,0,0,0,0,0,0,0,0 },
        new short[] { 5,13,36,36,43,43,43,43,18,18,18,18,18,18,18,18, 25,25,25,25,25,25,25,25,25,25,25,25,25,25,25,25, 0,0,0,0,0,0,0,0,0,0,0,0 },
    };

    // ---------------- alphabet 7 (2 tables) ----------------
    public static readonly int[] Code7 =
    {
        7, 1,2, 2,2, 3,2, 1,3, 1,4, 0,5, 1,5,
        7, 1,1, 1,2, 1,3, 1,4, 1,5, 0,6, 1,6,
    };
    public static readonly int[] Len7 = { 2,2,2,3,4,5,5,  1,2,3,4,5,6,6 };
    public static readonly int[] Delta7 = { 1, 0, -1, -1, -1, -1, -1 };
    public static readonly short[][] Dec7 =
    {
        new short[] { 45,53,36,36,27,27,27,27,2,2,2,2,2,2,2,2, 10,10,10,10,10,10,10,10,18,18,18,18,18,18,18,18, 0,0,0,0,0,0,0,0,0,0,0,0,0,0 },
        new short[] { -32736,37,28,28,19,19,19,19,10,10,10,10,10,10,10,10, 1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1, 5,6,0,0,0,0,0,0,0,0,0,0,0,0 },
    };

    // ---------------- alphabet 8 (2 tables defined; jxrlib always uses table 0) ----------------
    public static readonly int[] Code8 =
    {
        8, 2,2, 1,3, 1,5, 1,4, 3,2, 2,3, 0,5, 3,3,
        8, 1,3, 2,3, 1,4, 3,3, 4,3, 5,3, 0,4, 3,2,
    };
    public static readonly int[] Len8 = { 2,3,5,4,2,3,5,3,  3,3,4,3,3,3,4,2 };
    public static readonly int[] Delta8 = { -1, 0, 1, 1, -1, 0, 1, 1 };
    public static readonly short[][] Dec8 =
    {
        new short[] { 53,21,28,28,11,11,11,11,43,43,43,43,59,59,59,59, 2,2,2,2,2,2,2,2,34,34,34,34,34,34,34,34, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0 },
        new short[] { 52,52,20,20,3,3,3,3,11,11,11,11,27,27,27,27, 35,35,35,35,43,43,43,43,58,58,58,58,58,58,58,58, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0 },
    };

    // ---------------- alphabet 9 (2 tables) ----------------
    public static readonly int[] Code9 =
    {
        9, 2,3, 0,5, 2,4, 1,5, 2,5, 1,1, 3,3, 3,5, 3,4,
        9, 1,1, 1,3, 2,3, 1,4, 1,6, 3,3, 1,5, 0,7, 1,7,
    };
    public static readonly int[] Len9 = { 3,5,4,5,5,1,3,5,4,  1,3,3,4,6,3,5,7,7 };
    public static readonly int[] Delta9 = { 2, 2, 1, 1, -1, -2, -2, -2, -3 };
    public static readonly short[][] Dec9 =
    {
        new short[] { 13,29,37,61,20,20,68,68,3,3,3,3,51,51,51,51, 41,41,41,41,41,41,41,41,41,41,41,41,41,41,41,41, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0 },
        new short[] { -32736,53,28,28,11,11,11,11,19,19,19,19,43,43,43,43, 1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1, -32734,4,7,8,0,0,0,0,0,0,0,0,0,0,0,0, 0,0 },
    };

    // ---------------- alphabet 12 (5 tables, dual-discriminant) ----------------
    public static readonly int[] Code12 =
    {
        12, 1,5, 1,6, 0,7, 1,7, 4,5, 2,3, 5,5, 1,1, 6,5, 1,4, 7,5, 3,3,
        12, 2,4, 2,5, 0,6, 1,6, 3,4, 2,3, 3,5, 3,2, 3,3, 4,3, 1,5, 5,3,
        12, 3,2, 1,3, 0,7, 1,7, 1,5, 2,3, 2,7, 3,3, 4,3, 5,3, 3,7, 1,4,
        12, 1,3, 3,2, 0,7, 1,5, 2,5, 2,3, 1,7, 3,3, 3,5, 4,3, 1,6, 5,3,
        12, 2,3, 1,1, 1,7, 1,4, 2,7, 3,3, 0,8, 2,4, 3,7, 3,4, 1,8, 1,5,
    };
    public static readonly int[] Len12 =
    {
        5,6,7,7,5,3,5,1,5,4,5,3,
        4,5,6,6,4,3,5,2,3,3,5,3,
        2,3,7,7,5,3,7,3,3,3,7,4,
        3,2,7,5,5,3,7,3,5,3,6,3,
        3,1,7,4,7,3,8,4,7,4,8,5,
    };
    public static readonly int[] Delta12 =
    {
        1,1,1,1,1,0,0,-1,2,1,0,0,
        2,2,-1,-1,-1,0,-2,-1,0,0,-2,-1,
        -1,1,0,2,0,0,0,0,-2,0,1,1,
        0,1,0,1,-2,0,-1,-1,-2,-1,-2,-2,
    };
    public static readonly short[][] Dec12 =
    {
        new short[] { -32736,5,76,76,37,53,69,85,43,43,43,43,91,91,91,91, 57,57,57,57,57,57,57,57,57,57,57,57,57,57,57,57, -32734,1,2,3,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0 },
        new short[] { -32736,85,13,53,4,4,36,36,43,43,43,43,67,67,67,67, 75,75,75,75,91,91,91,91,58,58,58,58,58,58,58,58, 2,3,0,0,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0 },
        new short[] { -32736,37,92,92,11,11,11,11,43,43,43,43,59,59,59,59, 67,67,67,67,75,75,75,75,2,2,2,2,2,2,2,2, -32734,-32732,2,3,6,10,0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0 },
        new short[] { -32736,29,37,69,3,3,3,3,43,43,43,43,59,59,59,59, 75,75,75,75,91,91,91,91,10,10,10,10,10,10,10,10, -32734,10,2,6,0,0,0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0 },
        new short[] { -32736,93,28,28,60,60,76,76,3,3,3,3,43,43,43,43, 9,9,9,9,9,9,9,9,9,9,9,9,9,9,9,9, -32734,-32732,-32730,2,4,8,6,10,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0 },
    };

    /// <summary>Code table (flat, with markers) for an alphabet size.</summary>
    public static int[] CodeTable(int nSym) => nSym switch
    {
        4 => Code4, 5 => Code5, 6 => Code6, 7 => Code7, 8 => Code8, 9 => Code9, 12 => Code12,
        _ => throw new ArgumentOutOfRangeException(nameof(nSym)),
    };

    /// <summary>Per-symbol code lengths (g_Index*Table) for cross-checking.</summary>
    public static int[] LengthTable(int nSym) => nSym switch
    {
        4 => Len4, 5 => Len5, 6 => Len6, 7 => Len7, 8 => Len8, 9 => Len9, 12 => Len12,
        _ => throw new ArgumentOutOfRangeException(nameof(nSym)),
    };

    /// <summary>Decode lookup tables (one short[] per table index) for an alphabet size.</summary>
    public static short[][] DecodeTables(int nSym) => nSym switch
    {
        4 => Dec4, 5 => Dec5, 6 => Dec6, 7 => Dec7, 8 => Dec8, 9 => Dec9, 12 => Dec12,
        _ => throw new ArgumentOutOfRangeException(nameof(nSym)),
    };

    /// <summary>(code, length) for <paramref name="symbol"/> in table block <paramref name="tableIndex"/>.</summary>
    public static (int Code, int Length) GetCode(int nSym, int tableIndex, int symbol)
    {
        var t = CodeTable(nSym);
        int baseIdx = (nSym * 2 + 1) * tableIndex;
        return (t[baseIdx + symbol * 2 + 1], t[baseIdx + symbol * 2 + 2]);
    }
}
