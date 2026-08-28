namespace SharpAstro.Jxr;

/// <summary>
/// The adaptive-VLC table-switching state machine (jxrlib <c>CAdaptiveHuffman</c> /
/// <c>AdaptDiscriminant</c>, image/sys/adapthuff.c). Per coded symbol the entropy
/// coder accumulates a per-table "discriminant" (via the delta tables); then,
/// every context reset (16 MBs / tile boundary), <see cref="AdaptDiscriminant"/>
/// nudges the active VLC table up or down when the discriminant crosses a bound.
/// </summary>
/// <remarks>
/// This is the prime suspect for the original "garbage after the first block":
/// the subtle parts are the <see cref="Initialized"/> reset (the first call after
/// a context reset re-seeds the table index to <c>gSecondDisc[iSym]</c> and zeroes
/// both discriminants), the dual-discriminant handling for the 6- and 12-symbol
/// alphabets, the ±(THRESHOLD·MEMORY)=±64 clamp, and the open bounds at the
/// extreme table indices.
///
/// <para>This type owns the table-INDEX evolution and bounds. The concrete VLC
/// code / decode / delta tables (g4..g12*) and the per-symbol bit I/O live with
/// the entropy coder (segenc/segdec) and are wired in when that lands; the index
/// chosen here selects among them.</para>
/// </remarks>
internal sealed class AdaptiveHuffman
{
    // adapthuff.c:417-418 — indexed by alphabet size (iSym ∈ {4,5,6,7,8,9,12}).
    private static readonly int[] MaxTables = { 0, 0, 0, 0, 1, 2, 4, 2, 2, 2, 0, 0, 5 };
    private static readonly int[] SecondDisc = { 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1 };

    private const int Threshold = 8;
    private const int Memory = 8;

    public readonly int NSymbols;
    public int TableIndex;
    public int Discriminant;
    public int Discriminant1;
    public int UpperBound;
    public int LowerBound;
    public bool Initialized;

    public AdaptiveHuffman(int nSymbols) => NSymbols = nSymbols;

    /// <summary>Number of VLC tables for this alphabet (the table index ranges [0, this-1]).</summary>
    public int TableCount => MaxTables[NSymbols];

    /// <summary>
    /// adapthuff.c:413 AdaptDiscriminant — re-select the active VLC table from the
    /// accumulated discriminant(s) and update the bounds for the next interval.
    /// </summary>
    public void AdaptDiscriminant()
    {
        int iSym = NSymbols;
        bool change = false;

        if (!Initialized)
        {
            Initialized = true;
            Discriminant = Discriminant1 = 0;
            TableIndex = SecondDisc[iSym];
        }

        int dL = Discriminant;
        int dH = SecondDisc[iSym] != 0 ? Discriminant1 : Discriminant;

        if (dL < LowerBound) { TableIndex--; change = true; }
        else if (dH > UpperBound) { TableIndex++; change = true; }

        if (change)
        {
            Discriminant = 0;
            Discriminant1 = 0;
        }

        // Clamp both discriminants to ±(THRESHOLD·MEMORY) = ±64.
        Discriminant = Math.Clamp(Discriminant, -Threshold * Memory, Threshold * Memory);
        Discriminant1 = Math.Clamp(Discriminant1, -Threshold * Memory, Threshold * Memory);

        int t = TableIndex;
        LowerBound = t == 0 ? (-1 << 31) : -Threshold;
        UpperBound = t == MaxTables[iSym] - 1 ? (1 << 30) : Threshold;

        SelectTables(iSym, t);
    }

    // ---- active VLC table selection (the switch at the tail of AdaptDiscriminant) ----

    /// <summary>Code-table block index for the current table (iSym=8 always uses block 0).</summary>
    public int CodeTableIndex { get; private set; }

    /// <summary>Active decode lookup table for the current table.</summary>
    public short[] DecodeTable { get; private set; } = VlcTables.Dec4[0];

    private int[]? _delta;
    private int _deltaOffset;
    private int[]? _delta1;
    private int _delta1Offset;

    /// <summary>Discriminant delta for a coded symbol (added to <see cref="Discriminant"/> per symbol).</summary>
    public int Delta(int symbol) => _delta is null ? 0 : _delta[_deltaOffset + symbol];

    /// <summary>Second-discriminant delta (alphabets 6 and 12 only).</summary>
    public int Delta1(int symbol) => _delta1 is null ? 0 : _delta1[_delta1Offset + symbol];

    /// <summary>Raw (code, length) for a symbol in the currently selected table (jxrlib <c>m_pTable[sym*2+1/2]</c>).</summary>
    public (int Code, int Length) Code(int symbol) => VlcTables.GetCode(NSymbols, CodeTableIndex, symbol);

    /// <summary>Encode one symbol with the currently selected table.</summary>
    public void Encode(BitWriter w, int symbol) => VlcSymbolCodec.Encode(w, NSymbols, CodeTableIndex, symbol);

    /// <summary>Decode one symbol with the currently selected table.</summary>
    public int Decode(ref BitReader r) => VlcSymbolCodec.Decode(DecodeTable, ref r);

    private void SelectTables(int iSym, int t)
    {
        int maxT = MaxTables[iSym];
        int hiAdj = t + 1 == maxT ? 1 : 0;   // (t+1 == gMaxTables[iSym])
        int loAdj = t == 0 ? 1 : 0;          // (t == 0)
        _delta = _delta1 = null;
        _deltaOffset = _delta1Offset = 0;

        switch (iSym)
        {
            case 4:
                CodeTableIndex = 0; DecodeTable = VlcTables.Dec4[0];
                break;
            case 5:
                CodeTableIndex = t; DecodeTable = VlcTables.Dec5[t];
                _delta = VlcTables.Delta5;
                break;
            case 6:
                CodeTableIndex = t; DecodeTable = VlcTables.Dec6[t];
                _delta1 = VlcTables.Delta6; _delta1Offset = 6 * (t - hiAdj);
                _delta = VlcTables.Delta6; _deltaOffset = (t - 1 + loAdj) * 6;
                break;
            case 7:
                CodeTableIndex = t; DecodeTable = VlcTables.Dec7[t];
                _delta = VlcTables.Delta7;
                break;
            case 8: // jxrlib always uses block 0 here (the +t offset is commented out)
                CodeTableIndex = 0; DecodeTable = VlcTables.Dec8[0];
                break;
            case 9:
                CodeTableIndex = t; DecodeTable = VlcTables.Dec9[t];
                _delta = VlcTables.Delta9;
                break;
            case 12:
                CodeTableIndex = t; DecodeTable = VlcTables.Dec12[t];
                _delta1 = VlcTables.Delta12; _delta1Offset = 12 * (t - hiAdj);
                _delta = VlcTables.Delta12; _deltaOffset = (t - 1 + loAdj) * 12;
                break;
        }
    }
}
