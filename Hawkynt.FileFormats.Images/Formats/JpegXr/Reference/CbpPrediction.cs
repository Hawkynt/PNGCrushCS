namespace SharpAstro.Jxr;

/// <summary>
/// Adaptive model for CBP (coded block pattern) prediction — jxrlib
/// <c>CCBPModel</c> (common.h). Two saturating counters per channel group decide
/// which of three prediction transforms is applied: state 0 = XOR with the
/// spatial prediction, state 1 = pass-through, state 2 = bitwise inverse.
/// </summary>
internal sealed class CbpModel
{
    public readonly int[] Count0 = new int[2];
    public readonly int[] Count1 = new int[2];
    public readonly int[] State = new int[2];
}

/// <summary>
/// CBP prediction, ported from jxrlib's <c>predCBPCEnc</c> / <c>predCBPCDec</c>
/// (strPredQuantEnc.c / strPredQuantDec.c) for the full-resolution path. The CBP
/// (16 bits, one per 4×4 block) is predicted spatially within the macroblock plus
/// one bit from the top/left neighbor, then conditioned by the <see cref="CbpModel"/>
/// state. Encode∘decode reconstructs the original CBP and the model evolves
/// identically on both sides (driven by <c>NumOnes</c> of the original CBP).
/// </summary>
internal static class CbpPrediction
{
    private const int AvgNDiff = 3;
    private static readonly int[] NibbleOnes = { 0, 1, 1, 2, 1, 2, 2, 3, 1, 2, 2, 3, 2, 3, 3, 4 };

    /// <summary>jxrlib NumOnes — popcount of the low 16 bits.</summary>
    public static int NumOnes(int i)
    {
        int retval = 0;
        i &= 0xffff;
        while (i != 0) { retval += NibbleOnes[i & 0xf]; i >>= 4; }
        return retval;
    }

    // jxrlib SATURATE32 — clamp to [-16, 15].
    private static int Saturate(int x)
    {
        if ((uint)(x + 16) >= 32) x = x < 0 ? -16 : 15;
        return x;
    }

    private static void UpdateModel(CbpModel model, int c1, int nOrig)
    {
        model.Count0[c1] = Saturate(model.Count0[c1] + nOrig - AvgNDiff);
        model.Count1[c1] = Saturate(model.Count1[c1] + 16 - nOrig - AvgNDiff);

        if (model.Count0[c1] < 0)
            model.State[c1] = model.Count0[c1] < model.Count1[c1] ? 1 : 2;
        else if (model.Count1[c1] < 0)
            model.State[c1] = 2;
        else
            model.State[c1] = 0;
    }

    // The spatial within-MB prediction OR-ed onto the neighbor seed bit (encode side).
    private static int SpatialPrediction(int cbp, bool ctxLeft, bool ctxTop, int topCbp, int leftCbp)
    {
        int pred;
        if (ctxLeft) pred = ctxTop ? 1 : ((topCbp >> 10) & 1);
        else pred = (leftCbp >> 5) & 1;

        pred |= (cbp & 0x3300) << 2; // [8 9 12 13] -> [10 11 14 15]
        pred |= (cbp & 0xcc) << 6;   // [2 3 6 7]   -> [8 9 12 13]
        pred |= (cbp & 0x33) << 2;   // [0 1 4 5]   -> [2 3 6 7]
        pred |= (cbp & 0x11) << 1;   // [0 4]       -> [1 5]
        pred |= (cbp & 0x2) << 3;    // [1]         -> [4]
        return pred;
    }

    /// <summary>Encode-side CBP prediction — returns the value to transmit. <paramref name="channel"/> selects the model group (0 = luma, ≥1 = chroma).</summary>
    public static int PredictEnc(int cbp, bool ctxLeft, bool ctxTop, int topCbp, int leftCbp, int channel, CbpModel model)
    {
        int nOrig = NumOnes(cbp);
        int pred = SpatialPrediction(cbp, ctxLeft, ctxTop, topCbp, leftCbp);
        int c1 = channel == 0 ? 0 : 1;

        int retval = model.State[c1] switch
        {
            0 => pred ^ cbp,
            1 => cbp,
            _ => cbp ^ 0xffff,
        };

        UpdateModel(model, c1, nOrig);
        return retval;
    }

    /// <summary>Decode-side CBP prediction — reconstructs the original CBP from the transmitted value.</summary>
    public static int PredictDec(int cbp, bool ctxLeft, bool ctxTop, int topCbp, int leftCbp, int channel, CbpModel model)
    {
        int c1 = channel == 0 ? 0 : 1;

        if (model.State[c1] == 0)
        {
            if (ctxLeft) cbp ^= ctxTop ? 1 : ((topCbp >> 10) & 1);
            else cbp ^= (leftCbp >> 5) & 1;

            cbp ^= 0x02 & (cbp << 1); // 0 => 1
            cbp ^= 0x10 & (cbp << 3); // 1 => 4
            cbp ^= 0x20 & (cbp << 1); // 4 => 5
            cbp ^= (cbp & 0x33) << 2;
            cbp ^= (cbp & 0xcc) << 6;
            cbp ^= (cbp & 0x3300) << 2;
        }
        else if (model.State[c1] == 2)
        {
            cbp ^= 0xffff;
        }

        UpdateModel(model, c1, NumOnes(cbp));
        return cbp;
    }

    // ---------------------------------------------------------- chroma 420/422 (reduced CBP)
    // predCBPC420Enc/Dec (4-bit chroma CBP) and predCBPC422Enc/Dec (8-bit) — strPredQuantEnc.c
    // / strPredQuantDec.c. The two subsampled chroma planes share the chroma model slot (1); the
    // spatial prediction is the low-bit causal cascade of the reduced pattern, and the model
    // counter is driven by NumOnes(cbp) scaled to the 16-block range (×4 for 420, ×2 for 422).

    /// <summary>Encode-side reduced chroma CBP prediction (YUV420 if <paramref name="is420"/>, else YUV422).</summary>
    public static int PredictEncChroma(int cbp, bool ctxLeft, bool ctxTop, int topCbp, int leftCbp, bool is420, CbpModel model)
    {
        int mask = is420 ? 0xf : 0xff;
        int nOrig = NumOnes(cbp) * (is420 ? 4 : 2);

        int pred;
        if (ctxLeft) pred = ctxTop ? 1 : ((topCbp >> (is420 ? 2 : 6)) & 1);
        else pred = (leftCbp >> 1) & 1;

        pred |= (cbp & 0x1) << 1; // [0]->[1]
        pred |= (cbp & 0x3) << 2; // [0 1]->[2 3]
        if (!is420)
        {
            pred |= (cbp & 0xc) << 2;  // [2 3]->[4 5]
            pred |= (cbp & 0x30) << 2; // [4 5]->[6 7]
        }

        int retval = model.State[1] switch
        {
            0 => pred ^ cbp,
            1 => cbp,
            _ => cbp ^ mask,
        };

        UpdateModel(model, 1, nOrig);
        return retval;
    }

    /// <summary>Decode-side reduced chroma CBP prediction — exact inverse of <see cref="PredictEncChroma"/>.</summary>
    public static int PredictDecChroma(int cbp, bool ctxLeft, bool ctxTop, int topCbp, int leftCbp, bool is420, CbpModel model)
    {
        int mask = is420 ? 0xf : 0xff;

        if (model.State[1] == 0)
        {
            if (ctxLeft) cbp ^= ctxTop ? 1 : ((topCbp >> (is420 ? 2 : 6)) & 1);
            else cbp ^= (leftCbp >> 1) & 1;

            if (is420)
            {
                cbp ^= 0x02 & (cbp << 1);   // 0 => 1
                cbp ^= (cbp & 0x3) << 2;    // [0 1] -> [2 3]
            }
            else
            {
                cbp ^= (cbp & 0x1) << 1;    // [0]->[1]
                cbp ^= (cbp & 0x3) << 2;    // [0 1]->[2 3]
                cbp ^= (cbp & 0xc) << 2;    // [2 3]->[4 5]
                cbp ^= (cbp & 0x30) << 2;   // [4 5]->[6 7]
            }
        }
        else if (model.State[1] == 2)
        {
            cbp ^= mask;
        }

        UpdateModel(model, 1, NumOnes(cbp) * (is420 ? 4 : 2));
        return cbp;
    }
}
