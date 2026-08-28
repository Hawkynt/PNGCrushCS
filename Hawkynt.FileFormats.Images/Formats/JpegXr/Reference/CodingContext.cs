namespace SharpAstro.Jxr;

/// <summary>
/// The per-tile adaptive coding state, ported from jxrlib's <c>CCodingContext</c>
/// (strcodec.h) plus its allocation/reset (encode.c <c>AllocateCodingContextEnc</c>
/// / <c>ResetCodingContextEnc</c>, decode.c, and image.c <c>ResetCodingContext</c> /
/// <c>InitZigzagScan</c>). It owns the shared pool of adaptive-Huffman contexts, the
/// three adaptive scans, the DC/LP/AC bit-reduction models, and both CBP models.
/// One instance drives every macroblock in a tile; <see cref="Reset"/> re-seeds it
/// at a tile boundary.
/// </summary>
internal sealed class CodingContext
{
    // common.h: CONTEXTX = 8, CTDC = 5, NUMVLCTABLES = CONTEXTX*2 + CTDC = 21.
    public const int CtDc = 5;
    public const int ContextX = 8;
    public const int NumVlcTables = 21;

    // encode.c / decode.c aAlphabet[] — alphabet size of each m_pAHexpt context.
    // [0]=run(5) [1]=cbp444-chroma(4) [2]=DC-significance(8) [3]=DC-luma-abs(7) [4]=DC-chroma-abs(7)
    // [5..12]=LP block contexts (first12/index6/index6/first12/index6/index6/abs7/abs7)
    // [13..20]=HP block contexts (same layout as LP).
    private static readonly int[] Alphabet =
        { 5, 4, 8, 7, 7, 12, 6, 6, 12, 6, 6, 7, 7, 12, 6, 6, 12, 6, 6, 7, 7 };

    public readonly ColorFormat ColorFormat;
    public readonly int Channels;

    /// <summary>The shared adaptive-Huffman context pool (<c>m_pAHexpt</c>).</summary>
    public readonly AdaptiveHuffman[] AHexpt = new AdaptiveHuffman[NumVlcTables];

    /// <summary>CBP "number of coded blocks" symbol (<c>m_pAdaptHuffCBPCY1</c>, alphabet 5).</summary>
    public readonly AdaptiveHuffman CbpCy1 = new(5);

    /// <summary>Per-block CBP code (<c>m_pAdaptHuffCBPCY</c>, alphabet 9 for YUV, else 5).</summary>
    public readonly AdaptiveHuffman CbpCy;

    public readonly AdaptiveScan ScanLowpass = new(MacroblockLayout.ZigzagLowpass);
    public readonly AdaptiveScan ScanHoriz = new(MacroblockLayout.ScanHoriz);
    public readonly AdaptiveScan ScanVert = new(MacroblockLayout.ScanVert);

    public readonly AdaptiveModel ModelDc = new(Band.Dc);
    public readonly AdaptiveModel ModelLp = new(Band.Lp);
    public readonly AdaptiveModel ModelAc = new(Band.Ac);

    /// <summary>Adaptive LP-CBP "raw mode" counters (<c>m_iCBPCountZero/Max</c>).</summary>
    public int CbpCountZero;
    public int CbpCountMax;

    /// <summary>Adaptive HP-CBP prediction model (<c>m_aCBPModel</c>).</summary>
    public readonly CbpModel Cbp = new();

    /// <summary>Trimmed-flexbits width (set at construction; 0 for the default profile).</summary>
    public int TrimFlexBits { get; init; }

    /// <summary>BANDS_PRESENT == NO_FLEXBITS: the HP flexbits refinement plane is omitted
    /// entirely (the high-pass high part is still coded). Set at construction; false by default.</summary>
    public bool NoFlexBits { get; init; }

    public CodingContext(ColorFormat cf, int channels)
    {
        ColorFormat = cf;
        Channels = channels;

        bool small = cf == ColorFormat.YOnly || cf == ColorFormat.NComponent || cf == ColorFormat.Cmyk;
        CbpCy = new AdaptiveHuffman(small ? 5 : 9);
        for (var k = 0; k < NumVlcTables; k++)
            AHexpt[k] = new AdaptiveHuffman(Alphabet[k]);

        Reset();
    }

    /// <summary>
    /// jxrlib <c>ResetCodingContextEnc</c>/<c>Dec</c>: clear the per-table init flag,
    /// (re)seed every VLC table via <see cref="AdaptiveHuffman.AdaptDiscriminant"/>,
    /// restore the zigzag scans, and reset the bit-reduction + CBP models. Encode and
    /// decode reset identically, so a paired pair of contexts evolves in lock-step.
    /// </summary>
    public void Reset()
    {
        CbpCy.Initialized = false;
        CbpCy1.Initialized = false;
        foreach (var ah in AHexpt) ah.Initialized = false;

        // AdaptLowpassEnc/Dec adapts [0, CTDC+CONTEXTX); AdaptHighpassEnc/Dec adapts the
        // two CBP contexts + [CTDC+CONTEXTX, NUMVLCTABLES). Net effect: seed each once.
        foreach (var ah in AHexpt) ah.AdaptDiscriminant();
        CbpCy.AdaptDiscriminant();
        CbpCy1.AdaptDiscriminant();

        ScanLowpass.InitZigzag(MacroblockLayout.ZigzagLowpass);
        ScanHoriz.InitZigzag(MacroblockLayout.ScanHoriz);
        ScanVert.InitZigzag(MacroblockLayout.ScanVert);

        // image.c ResetCodingContext — models + CBP state.
        ResetModel(ModelDc, 8);
        ResetModel(ModelLp, 4);
        ResetModel(ModelAc, 0);
        CbpCountMax = CbpCountZero = 1;
        Cbp.Count0[0] = Cbp.Count0[1] = -4;
        Cbp.Count1[0] = Cbp.Count1[1] = 4;
        Cbp.State[0] = Cbp.State[1] = 0;
    }

    private static void ResetModel(AdaptiveModel m, int init)
    {
        m.FlcState[0] = m.FlcState[1] = 0;
        m.FlcBits[0] = m.FlcBits[1] = init;
    }

    /// <summary>
    /// jxrlib <c>AdaptLowpassEnc</c>/<c>Dec</c> (segenc.c/segdec.c): re-adapt the DC + LP
    /// VLC tables — contexts <c>[0, CTDC+CONTEXTX)</c>, which includes the shared run
    /// context, the DC significance/abs contexts, and the LP block contexts. Called at
    /// the END of the LP band on macroblocks where <c>m_bResetContext</c> holds
    /// (<c>(mbX - tileX) &amp; 0xf == 0</c>), so the freshly-adapted tables apply to the
    /// macroblocks that follow within the 16-wide group.
    /// </summary>
    public void AdaptLowpass()
    {
        for (var k = 0; k < CtDc + ContextX; k++) AHexpt[k].AdaptDiscriminant();
    }

    /// <summary>
    /// jxrlib <c>AdaptHighpassEnc</c>/<c>Dec</c>: re-adapt the two CBP contexts and the HP
    /// block contexts <c>[CTDC+CONTEXTX, NUMVLCTABLES)</c>. Called at the END of the HP
    /// band on <c>m_bResetContext</c> macroblocks (after <see cref="AdaptLowpass"/> for
    /// the same MB, so the LP-adapted shared run context is already in place for HP).
    /// </summary>
    public void AdaptHighpass()
    {
        CbpCy.AdaptDiscriminant();
        CbpCy1.AdaptDiscriminant();
        for (var k = 0; k < ContextX; k++) AHexpt[k + CtDc + ContextX].AdaptDiscriminant();
    }
}
