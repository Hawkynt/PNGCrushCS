namespace SharpAstro.Jxr;

/// <summary>
/// Frequency-domain tile coder for <b>YUV444</b>: orchestrates per-macroblock
/// prediction (DC / AD / AC + CBP) and band entropy coding across a grid of
/// macroblocks, maintaining the <c>CWMIPredInfo</c> neighbor buffers (current +
/// previous MB row) and the per-MB boundary context. Ports the per-MB driver
/// sequence from jxrlib strenc.c / strdec.c:
/// <code>
///   encode: predMacroblockEnc (subtract) -> EncodeMacroblockDC/Lowpass/Highpass
///   decode: DecodeMacroblockDC/Lowpass -> predDCACDec (add) -> DecodeMacroblockHighpass -> predACDec (add)
/// </code>
/// It operates on already-quantized coefficient macroblocks — the signal path
/// (color → overlap → transform → quant) is layered on in the next rung — so an
/// encode→decode of a tile reproduces the quantized coefficients (hpQp = 1).
///
/// <para>The prediction primitives (<see cref="Prediction"/>) and band coders
/// (<see cref="MacroblockCoder"/>) were each validated in earlier rungs; this is the
/// "Rung 7a" assembly that wires neighbor prediction across a real MB grid.</para>
/// </summary>
internal sealed class TileCoder
{
    private const int QIndexLp = 0; // single QP for now; multi-QP threads the real index in a later rung
    private static readonly PredInfo Empty = new();

    private readonly int Channels;
    private readonly ColorFormat Cf;
    private readonly bool _reducedChroma; // YUV420/422: chroma channels use the reduced predictors
    private readonly bool _is420;
    private PredInfo[][] _cur;   // [ch][mbX] current MB row
    private PredInfo[][] _prev;  // [ch][mbX] previous MB row
    private readonly int _mbWidth;

    public TileCoder(int mbWidth, int channels = 3, ColorFormat cf = ColorFormat.Yuv444)
    {
        _mbWidth = mbWidth;
        Channels = channels;
        Cf = cf;
        _reducedChroma = cf is ColorFormat.Yuv420 or ColorFormat.Yuv422;
        _is420 = cf == ColorFormat.Yuv420;
        _cur = NewRow(mbWidth, channels);
        _prev = NewRow(mbWidth, channels);
    }

    // jxrlib m_bResetContext (strcodec.c:163-169): re-adapt the VLC tables (AdaptDiscriminant)
    // at the start of every 16-wide group AND at the last column of each row (single-tile case).
    private bool ResetContext(int mbX) => (mbX & 0xf) == 0 || mbX + 1 == _mbWidth;

    // jxrlib m_bResetRGITotals (strcodec.c:163): reset the adaptive-scan totals at the start of
    // every 16-wide group (each row's first MB) — no last-column override.
    private static bool ResetTotals(int mbX) => (mbX & 0xf) == 0;

    private static PredInfo[][] NewRow(int w, int channels)
    {
        var rows = new PredInfo[channels][];
        for (var c = 0; c < channels; c++)
        {
            rows[c] = new PredInfo[w];
            for (var x = 0; x < w; x++) rows[c][x] = new PredInfo();
        }
        return rows;
    }

    /// <summary>jxrlib <c>advanceOneMBRow</c> — swap current/previous PredInfo rows at the end of an MB row.</summary>
    public void AdvanceRow() => (_cur, _prev) = (_prev, _cur);

    // ===================================================================== encode

    public void EncodeMacroblock(CodingContext ctx, Macroblock mb, int mbX, int mbY,
                                 BitWriter dc, BitWriter lp, BitWriter ac, BitWriter fl)
    {
        bool ctxLeft = mbX == 0, ctxTop = mbY == 0;

        // predMacroblockEnc: pick modes, store pre-prediction neighbor info, then subtract.
        int mode = DcAcMode(mb, mbX, ctxLeft, ctxTop);
        int dcMode = mode & 0x3, adMode = (mode >> 2) & 0x3;
        int acMode = Prediction.GetAcPredMode(mb.BlockDc, Cf);
        mb.Orientation = 2 - acMode;

        UpdatePredInfo(mb, mbX); // pre-prediction DC/AD/QP

        if (Trace.On && mbX < 4 && mbY < 2)
            Trace.Mb("ENC", mbX, mbY, mode, dcMode, adMode, acMode, mb, pre: true);

        for (var ch = 0; ch < Channels; ch++)
        {
            if (_reducedChroma && ch > 0)
            {
                Prediction.DcAdPredictEncChroma(mb.BlockDc[ch], dcMode, adMode, Left(ch, mbX), _prev[ch][mbX], _is420);
                Prediction.AcPredictEncChroma(mb.Plane[ch], acMode, _is420);
            }
            else
            {
                Prediction.DcAdPredictEnc(mb.BlockDc[ch], dcMode, adMode, Left(ch, mbX), _prev[ch][mbX]);
                Prediction.AcPredictEnc(mb.Plane[ch], acMode);
            }
        }

        if (Trace.On && mbX < 4 && mbY < 2)
            Trace.Mb("ENC", mbX, mbY, mode, dcMode, adMode, acMode, mb, pre: false);

        var (leftCbp, topCbp) = NeighborCbp(mbX);
        bool resetContext = ResetContext(mbX), resetTotals = ResetTotals(mbX);
        MacroblockCoder.EncodeDc(ctx, mb, dc);
        MacroblockCoder.EncodeLowpass(ctx, mb, lp, resetContext, resetTotals);
        MacroblockCoder.EncodeHighpass(ctx, mb, ac, fl, ctxLeft, ctxTop, leftCbp, topCbp, resetContext, resetTotals);

        if (Trace.On && mbX < 4 && mbY < 2)
            Trace.Model(mbX, mbY, ctx);

        for (var ch = 0; ch < Channels; ch++) _cur[ch][mbX].Cbp = mb.Cbp[ch]; // predCBPEnc stores the CBP
    }

    // ===================================================================== decode

    public void DecodeMacroblock(CodingContext ctx, Macroblock mb, int mbX, int mbY,
                                 ref BitReader dc, ref BitReader lp, ref BitReader ac, ref BitReader fl, int hpQp = 1,
                                 int[]? hpQpCh = null)
    {
        bool ctxLeft = mbX == 0, ctxTop = mbY == 0;

        bool resetContext = ResetContext(mbX), resetTotals = ResetTotals(mbX);
        MacroblockCoder.DecodeDc(ctx, mb, ref dc);
        MacroblockCoder.DecodeLowpass(ctx, mb, ref lp, resetContext, resetTotals);

        // predDCACDec: pick modes, add DC/AD, then derive the orientation from reconstructed LP.
        int mode = DcAcMode(mb, mbX, ctxLeft, ctxTop);
        int dcMode = mode & 0x3, adMode = (mode >> 2) & 0x3;
        for (var ch = 0; ch < Channels; ch++)
        {
            if (_reducedChroma && ch > 0)
                Prediction.DcAdPredictDecChroma(mb.BlockDc[ch], dcMode, adMode, Left(ch, mbX), _prev[ch][mbX], _is420);
            else
                Prediction.DcAdPredictDec(mb.BlockDc[ch], dcMode, adMode, Left(ch, mbX), _prev[ch][mbX]);
        }
        mb.Orientation = 2 - Prediction.GetAcPredMode(mb.BlockDc, Cf);

        var (leftCbp, topCbp) = NeighborCbp(mbX);
        MacroblockCoder.DecodeHighpass(ctx, mb, ref ac, ref fl, hpQp, ctxLeft, ctxTop, leftCbp, topCbp, resetContext, resetTotals, hpQpCh);
        for (var ch = 0; ch < Channels; ch++) _cur[ch][mbX].Cbp = mb.Cbp[ch]; // predCBPDec stores the CBP

        // predACDec: add AC, then store reconstructed neighbor info.
        int acMode = 2 - mb.Orientation;
        for (var ch = 0; ch < Channels; ch++)
        {
            if (_reducedChroma && ch > 0) Prediction.AcPredictDecChroma(mb.Plane[ch], acMode, _is420);
            else Prediction.AcPredictDec(mb.Plane[ch], acMode);
        }

        UpdatePredInfo(mb, mbX); // reconstructed DC/AD/QP
    }

    // ===================================================================== helpers

    private PredInfo Left(int ch, int mbX) => mbX > 0 ? _cur[ch][mbX - 1] : Empty;

    // jxrlib getDCACPredMode: DC mode from boundary flags / neighbor-DC orientation, AD mode from QP match.
    private int DcAcMode(Macroblock mb, int mbX, bool ctxLeft, bool ctxTop)
    {
        var lY = Left(0, mbX);
        var tY = _prev[0][mbX];
        var tlY = mbX > 0 ? _prev[0][mbX - 1] : Empty;
        (int left, int top, int topLeft)[]? chroma = null;
        if (Channels >= 3) // Y-only / N-channel: getDCACPredMode ignores chroma DC
        {
            chroma = new[]
            {
                (Left(1, mbX).Dc, _prev[1][mbX].Dc, mbX > 0 ? _prev[1][mbX - 1].Dc : 0),
                (Left(2, mbX).Dc, _prev[2][mbX].Dc, mbX > 0 ? _prev[2][mbX - 1].Dc : 0),
            };
        }
        return Prediction.GetDcAcPredMode(ctxLeft, ctxTop, Cf,
            lY.Dc, tY.Dc, tlY.Dc, QIndexLp, lY.QpIndex, tY.QpIndex, chroma);
    }

    // jxrlib updatePredInfo: store DC, QP index, and the copyAC (first row/column AD) of the DC block.
    private void UpdatePredInfo(Macroblock mb, int mbX)
    {
        for (var ch = 0; ch < Channels; ch++)
        {
            var pi = _cur[ch][mbX];
            pi.Dc = mb.BlockDc[ch][0];
            pi.QpIndex = QIndexLp;
            if (_reducedChroma && ch > 0) Prediction.CopyAcChroma(mb.BlockDc[ch], pi.Ad, _is420);
            else Prediction.CopyAc(mb.BlockDc[ch], pi.Ad);
        }
    }

    // Neighbor CBPs for the HP CBP prediction (left = current row, top = previous row).
    private (int[] left, int[] top) NeighborCbp(int mbX)
    {
        var left = new int[Channels];
        var top = new int[Channels];
        for (var ch = 0; ch < Channels; ch++)
        {
            left[ch] = mbX > 0 ? _cur[ch][mbX - 1].Cbp : 0;
            top[ch] = _prev[ch][mbX].Cbp;
        }
        return (left, top);
    }
}
