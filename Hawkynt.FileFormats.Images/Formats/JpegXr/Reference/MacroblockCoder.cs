using System.Numerics;

namespace SharpAstro.Jxr;

/// <summary>
/// The per-macroblock band entropy coder for <b>YUV444</b>, ported from jxrlib's
/// <c>EncodeMacroblockDC/Lowpass/Highpass</c> + <c>CodeCBP</c>/<c>CodeCoeffs</c>
/// (segenc.c) and their decode mirrors <c>DecodeMacroblock*</c> + <c>DecodeCBP</c>/
/// <c>DecodeCoeffs</c> (segdec.c). It assembles the leaf coders proved in earlier
/// rungs — adaptive VLC (<see cref="VlcSymbolCodec"/>), run/level
/// (<see cref="CoefficientSyntax"/>), CBP prediction (<see cref="CbpPrediction"/>),
/// model bits (<see cref="ModelBits"/>), adaptive scan (<see cref="AdaptiveScan"/>) —
/// over the shared <see cref="CodingContext"/> pool into a complete macroblock.
///
/// <para>This is "Rung 6": our-encode ↔ our-decode of a single macroblock's
/// quantized/predicted residual coefficients (DC + LP + HP + CBP), no oracle. The
/// DC/LP bands serialize the <see cref="Macroblock.BlockDc"/> arrays directly; the HP
/// band serializes the <see cref="Macroblock.Plane"/> coefficients and bakes the HP
/// dequantizer into the decode (so a round-trip is bit-exact when <c>hpQp == 1</c>).
/// The DC/LP/HP bands are written to three separate streams (the SPATIAL multiplexing
/// into one codestream is a tiling concern for the next rung).</para>
/// </summary>
internal static class MacroblockCoder
{
    private const int CtDc = CodingContext.CtDc;            // 5
    private const int CtHp = CodingContext.CtDc + CodingContext.ContextX; // 13

    // The two chroma planes of YUV420/422 share one interleaved LP run/level stream; aRemap maps
    // the joint-stream index to the per-plane LP coefficient position (segenc.c/segdec.c). 420 uses
    // the tail {1,2,3,5,6,7} (offset 1, only the first 3 are reached), 422 the full 7-entry table.
    private static readonly int[] LpChromaRemap = { 4, 1, 2, 3, 5, 6, 7 };

    /// <summary>The three-channel YUV formats that share the joint (Y,U,V) DC path and chroma LP interleave.</summary>
    private static bool IsYuv(ColorFormat cf) =>
        cf is ColorFormat.Yuv444 or ColorFormat.Yuv422 or ColorFormat.Yuv420;

    // ----------------------------------------------------------------- public API

    /// <summary>
    /// Encode <paramref name="mb"/> (YUV444) into the DC/LP/AC/FL streams (single isolated MB,
    /// no neighbors). For a single MB the AC model bits are 0, so the flexbits stream stays empty.
    /// </summary>
    public static void Encode(CodingContext ctx, Macroblock mb,
                              BitWriter dc, BitWriter lp, BitWriter ac, BitWriter fl,
                              bool ctxLeft = true, bool ctxTop = true)
    {
        RequireSupported(ctx);
        EncodeDc(ctx, mb, dc);
        EncodeLowpass(ctx, mb, lp, resetContext: true, resetTotals: true);
        EncodeHighpass(ctx, mb, ac, fl, ctxLeft, ctxTop, null, null, resetContext: true, resetTotals: true);
    }

    /// <summary>Decode a YUV444 macroblock from the DC/LP/AC/FL streams into <paramref name="mb"/> (single isolated MB).</summary>
    public static void Decode(CodingContext ctx, Macroblock mb,
                              ref BitReader dc, ref BitReader lp, ref BitReader ac, ref BitReader fl,
                              int hpQp = 1, bool ctxLeft = true, bool ctxTop = true)
    {
        RequireSupported(ctx);
        DecodeDc(ctx, mb, ref dc);
        DecodeLowpass(ctx, mb, ref lp, resetContext: true, resetTotals: true);
        DecodeHighpass(ctx, mb, ref ac, ref fl, hpQp, ctxLeft, ctxTop, null, null, resetContext: true, resetTotals: true);
    }

    private static void RequireSupported(CodingContext ctx)
    {
        bool yuv = IsYuv(ctx.ColorFormat) && ctx.Channels == 3; // 444 / 422 / 420
        bool yOnly = ctx.ColorFormat == ColorFormat.YOnly && ctx.Channels == 1;
        if (!yuv && !yOnly)
            throw new NotSupportedException("MacroblockCoder supports YUV444/422/420 (3 channels) and Y-only (1 channel).");
    }

    // ===================================================================== DC band

    internal static void EncodeDc(CodingContext ctx, Macroblock mb, BitWriter w)
    {
        var model = ctx.ModelDc;
        var lapMean = new int[2];

        // Y_ONLY / CMYK / N_CHANNEL DC: per-channel raw significance bit + abs level (AHexpt[3]).
        // The three-channel YUV formats (444/422/420) all use the joint (Y,U,V) DC path below.
        if (!IsYuv(ctx.ColorFormat))
        {
            int mbits = model.FlcBits[0];
            for (var ch = 0; ch < ctx.Channels; ch++)
            {
                int dc = mb.BlockDc[ch][0];
                int absDc = Math.Abs(dc);
                int q = absDc >> mbits;
                if (q != 0)
                {
                    w.WriteBit(true);
                    CoefficientSyntax.EncodeAbsLevel(w, ctx.AHexpt[3], q + 1);
                    lapMean[ch == 0 ? 0 : 1]++;
                }
                else w.WriteBit(false);
                w.WriteBits((uint)absDc, mbits);
                if (absDc != 0) w.WriteBit(dc < 0);
                mbits = model.FlcBits[1];
            }
            ModelBits.UpdateMb(ctx.ColorFormat, ctx.Channels, lapMean, model);
            return;
        }

        int dcY = mb.BlockDc[0][0], dcU = mb.BlockDc[1][0], dcV = mb.BlockDc[2][0];
        int qY = Math.Abs(dcY), qU = Math.Abs(dcU), qV = Math.Abs(dcV);

        int mbY = model.FlcBits[0];
        if (mbY != 0) qY >>= mbY;
        int mbC = model.FlcBits[1];
        if (mbC != 0) { qU >>= mbC; qV >>= mbC; }

        int index = (qY != 0 ? 4 : 0) + (qU != 0 ? 2 : 0) + (qV != 0 ? 1 : 0);
        ctx.AHexpt[2].Encode(w, index); // YUV 3D significance (alphabet 8, no discriminant)

        // luminance DC
        if (qY != 0) { CoefficientSyntax.EncodeAbsLevel(w, ctx.AHexpt[3], qY + 1); lapMean[0]++; }
        w.WriteBits((uint)Math.Abs(dcY), mbY);
        if (dcY != 0) w.WriteBit(dcY < 0);

        // chrominance DC (U then V share AHexpt[4])
        if (qU != 0) { CoefficientSyntax.EncodeAbsLevel(w, ctx.AHexpt[4], qU + 1); lapMean[1]++; }
        w.WriteBits((uint)Math.Abs(dcU), mbC);
        if (dcU != 0) w.WriteBit(dcU < 0);

        if (qV != 0) { CoefficientSyntax.EncodeAbsLevel(w, ctx.AHexpt[4], qV + 1); lapMean[1]++; }
        w.WriteBits((uint)Math.Abs(dcV), mbC);
        if (dcV != 0) w.WriteBit(dcV < 0);

        ModelBits.UpdateMb(ctx.ColorFormat, ctx.Channels, lapMean, model);
    }

    internal static void DecodeDc(CodingContext ctx, Macroblock mb, ref BitReader r)
    {
        var model = ctx.ModelDc;
        var lapMean = new int[2];
        for (var i = 0; i < ctx.Channels; i++) Array.Clear(mb.BlockDc[i]);

        // Y_ONLY / CMYK / N_CHANNEL DC: per-channel raw significance bit + abs level (AHexpt[3]).
        // The three-channel YUV formats (444/422/420) all use the joint (Y,U,V) DC path below.
        if (!IsYuv(ctx.ColorFormat))
        {
            int mbits0 = model.FlcBits[0];
            for (var ch = 0; ch < ctx.Channels; ch++)
            {
                int q = 0;
                if (r.ReadBit())
                {
                    q = CoefficientSyntax.DecodeAbsLevel(ref r, ctx.AHexpt[3]) - 1;
                    lapMean[ch == 0 ? 0 : 1]++;
                }
                if (mbits0 != 0) q = (q << mbits0) | (int)r.ReadBits(mbits0);
                if (q != 0 && r.ReadBit()) q = -q;
                mb.BlockDc[ch][0] = q;
                mbits0 = model.FlcBits[1];
            }
            ModelBits.UpdateMb(ctx.ColorFormat, ctx.Channels, lapMean, model);
            return;
        }

        int index = ctx.AHexpt[2].Decode(ref r);
        int qY = index >> 2, qU = (index >> 1) & 1, qV = index & 1;

        int mbY = model.FlcBits[0];
        if (qY != 0) { qY = CoefficientSyntax.DecodeAbsLevel(ref r, ctx.AHexpt[3]) - 1; lapMean[0]++; }
        if (mbY != 0) qY = (qY << mbY) | (int)r.ReadBits(mbY);
        if (qY != 0 && r.ReadBit()) qY = -qY;
        mb.BlockDc[0][0] = qY;

        int mbC = model.FlcBits[1];
        if (qU != 0) { qU = CoefficientSyntax.DecodeAbsLevel(ref r, ctx.AHexpt[4]) - 1; lapMean[1]++; }
        if (mbC != 0) qU = (qU << mbC) | (int)r.ReadBits(mbC);
        if (qU != 0 && r.ReadBit()) qU = -qU;
        mb.BlockDc[1][0] = qU;

        if (qV != 0) { qV = CoefficientSyntax.DecodeAbsLevel(ref r, ctx.AHexpt[4]) - 1; lapMean[1]++; }
        if (mbC != 0) qV = (qV << mbC) | (int)r.ReadBits(mbC);
        if (qV != 0 && r.ReadBit()) qV = -qV;
        mb.BlockDc[2][0] = qV;

        ModelBits.UpdateMb(ctx.ColorFormat, ctx.Channels, lapMean, model);
    }

    // ===================================================================== LP band

    internal static void EncodeLowpass(CodingContext ctx, Macroblock mb, BitWriter w, bool resetContext, bool resetTotals)
    {
        if (resetTotals) ctx.ScanLowpass.Reset(); // jxrlib m_bResetRGITotals — at the START of the LP band
        if (ctx.ColorFormat is ColorFormat.Yuv420 or ColorFormat.Yuv422)
        {
            EncodeLowpassChroma(ctx, mb, w, resetContext);
            return;
        }
        var model = ctx.ModelLp;
        var scan = ctx.ScanLowpass;
        var lapMean = new int[2];
        int fullCh = ctx.Channels; // iFullChannels (YUV420/422 not supported, so == iChannels)
        var rl = new int[fullCh][];
        var residual = new int[fullCh][];
        var numCoeffs = new int[fullCh];

        int mbits = model.FlcBits[0];
        for (var ch = 0; ch < fullCh; ch++)
        {
            rl[ch] = new int[32];
            residual[ch] = new int[16];
            numCoeffs[ch] = ScanCoefficients(mb.BlockDc[ch], 0, residual[ch], scan, mbits, rl[ch]);
            mbits = model.FlcBits[1];
        }

        if (ctx.ColorFormat == ColorFormat.Yuv444)
        {
            // YUV444 lowpass CBP (3-bit channel pattern), with the adaptive "raw mode" model.
            const int max = 3 * 4 - 5; // 7
            int cbp = (numCoeffs[0] > 0 ? 1 : 0) + (numCoeffs[1] > 0 ? 2 : 0) + (numCoeffs[2] > 0 ? 4 : 0);
            int countM = ctx.CbpCountMax, countZ = ctx.CbpCountZero;
            if (countZ <= 0 || countM < 0)
            {
                int val = countM < countZ ? max - cbp : cbp;
                if (val == 0) w.WriteBits(0, 1);
                else if (val == 1) w.WriteBits((3 + 1) & 0x6, 3);
                else w.WriteBits((uint)(val + max + 1), 3 + 1);
            }
            else
            {
                w.WriteBits((uint)cbp, 3);
            }
            ctx.CbpCountMax = Clamp8(countM + 1 - 4 * (cbp == max ? 1 : 0));
            ctx.CbpCountZero = Clamp8(countZ + 1 - 4 * (cbp == 0 ? 1 : 0));
        }
        else
        {
            // Y_ONLY / N_CHANNEL: one raw significance bit per channel (no adaptive raw mode).
            for (var ch = 0; ch < ctx.Channels; ch++)
                w.WriteBit(numCoeffs[ch] > 0);
        }

        mbits = model.FlcBits[0];
        for (var ch = 0; ch < fullCh; ch++)
        {
            if (numCoeffs[ch] != 0)
            {
                lapMean[ch == 0 ? 0 : 1] += numCoeffs[ch];
                EncodeBlock(ch > 0, rl[ch], numCoeffs[ch], ctx, CtDc, w, 1);
            }
            if (mbits != 0)
                for (var k = 1; k < 16; k++)
                    w.WriteBits((uint)(residual[ch][k] >> 1), mbits + (residual[ch][k] & 1));
            mbits = model.FlcBits[1];
        }

        ModelBits.UpdateMb(ctx.ColorFormat, ctx.Channels, lapMean, model);
        if (resetContext) ctx.AdaptLowpass();
    }

    internal static void DecodeLowpass(CodingContext ctx, Macroblock mb, ref BitReader r, bool resetContext, bool resetTotals)
    {
        if (resetTotals) ctx.ScanLowpass.Reset(); // jxrlib m_bResetRGITotals — at the START of the LP band
        if (ctx.ColorFormat is ColorFormat.Yuv420 or ColorFormat.Yuv422)
        {
            DecodeLowpassChroma(ctx, mb, ref r, resetContext);
            return;
        }
        var model = ctx.ModelLp;
        var scan = ctx.ScanLowpass;
        var lapMean = new int[2];
        var rl = new int[32];
        int fullCh = ctx.Channels; // iFullPlanes (YUV420/422 not supported, so == iChannels)

        int cbp;
        if (ctx.ColorFormat == ColorFormat.Yuv444)
        {
            // YUV444 lowpass CBP.
            int countM = ctx.CbpCountMax, countZ = ctx.CbpCountZero;
            const int max = 3 * 4 - 5; // 7
            if (countZ <= 0 || countM < 0)
            {
                cbp = 0;
                if (r.ReadBit())
                {
                    cbp = 1;
                    int k = (int)r.ReadBits(3 - 1);
                    if (k != 0) cbp = k * 2 + (int)r.ReadBits(1);
                }
                if (countM < countZ) cbp = max - cbp;
            }
            else
            {
                cbp = (int)r.ReadBits(3);
            }
            ctx.CbpCountMax = Clamp8(countM + 1 - 4 * (cbp == max ? 1 : 0));
            ctx.CbpCountZero = Clamp8(countZ + 1 - 4 * (cbp == 0 ? 1 : 0));
        }
        else
        {
            // Y_ONLY / N_CHANNEL: one raw significance bit per channel.
            cbp = 0;
            for (var ch = 0; ch < ctx.Channels; ch++)
                cbp |= (r.ReadBit() ? 1 : 0) << ch;
        }

        int mbits = model.FlcBits[0];
        for (var ch = 0; ch < fullCh; ch++)
        {
            int[] coeffs = mb.BlockDc[ch];
            if ((cbp & 1) != 0)
            {
                int n = DecodeBlock(ch > 0, rl, ctx, CtDc, ref r, 1);
                lapMean[ch == 0 ? 0 : 1] += n;
                int idx = 1;
                for (var k = 0; k < n; k++)
                {
                    idx += rl[k * 2];
                    coeffs[scan.Scan[idx]] = rl[k * 2 + 1];
                    scan.Visit(idx);
                    idx++;
                }
            }

            if (mbits != 0)
            {
                int mask = (1 << mbits) - 1;
                for (var k = 1; k < 16; k++)
                {
                    if (coeffs[k] != 0)
                    {
                        int r1 = (int)BitOperations.RotateLeft((uint)coeffs[k], mbits);
                        coeffs[k] = (r1 ^ (int)r.ReadBits(mbits)) - (r1 & mask);
                    }
                    else
                    {
                        uint v = r.PeekBits(mbits + 1);
                        int val = (int)((v >> 1) ^ (uint)-(int)(v & 1)) + (int)(v & 1);
                        coeffs[k] = val;
                        r.SkipBits(mbits + (val != 0 ? 1 : 0));
                    }
                }
            }
            mbits = model.FlcBits[1];
            cbp >>= 1;
        }

        ModelBits.UpdateMb(ctx.ColorFormat, ctx.Channels, lapMean, model);
        if (resetContext) ctx.AdaptLowpass();
    }

    // ----------------------------------------------------------- LP band (chroma 420/422)

    // segenc.c EncodeMacroblockLowpass, YUV420/422 branch. A luma pass (channel 0, standard scan +
    // residual refinement) and a single joint U+V pass: the two chroma planes are interleaved into
    // one run/level stream via LpChromaRemap (iLocation 10 for 420 / 2 for 422), with an
    // interleaved-sign refinement. The LP-CBP is a 2-channel (iFullChannels=2, iMax=3) pattern.
    private static void EncodeLowpassChroma(CodingContext ctx, Macroblock mb, BitWriter w, bool resetContext)
    {
        var model = ctx.ModelLp;
        var scan = ctx.ScanLowpass;
        var lapMean = new int[2];
        bool is420 = ctx.ColorFormat == ColorFormat.Yuv420;
        int remapOff = is420 ? 1 : 0;
        int count = is420 ? 6 : 14;

        // luma scan (iFullChannels begins at 1)
        int mbitsY = model.FlcBits[0];
        var rlY = new int[32];
        var residualY = new int[16];
        int numY = ScanCoefficients(mb.BlockDc[0], 0, residualY, scan, mbitsY, rlY);

        // gather the two chroma planes into one interleaved run/level stream
        int mbitsC = model.FlcBits[1];
        var rlC = new int[32];
        var bufU = new int[16];
        var bufV = new int[16];
        int run = 0, numC = 0;
        for (var k = 0; k < count; k++)
        {
            int idx = LpChromaRemap[remapOff + (k >> 1)];
            int dc = mb.BlockDc[(k & 1) + 1][idx];
            int val = Math.Abs(dc) >> mbitsC;
            if ((k & 1) == 0) bufU[idx] = val; else bufV[idx] = val;
            if (val != 0) { rlC[numC * 2] = run; rlC[numC * 2 + 1] = dc < 0 ? -val : val; numC++; run = 0; }
            else run++;
        }

        // LP-CBP (iFullChannels = 2, iMax = 3)
        const int max = 2 * 4 - 5; // 3
        int cbp = (numY > 0 ? 1 : 0) + (numC > 0 ? 2 : 0);
        int countM = ctx.CbpCountMax, countZ = ctx.CbpCountZero;
        if (countZ <= 0 || countM < 0)
        {
            int val = countM < countZ ? max - cbp : cbp;
            if (val == 0) w.WriteBits(0, 1);
            else if (val == 1) w.WriteBits((2 + 1) & 0x6, 2);     // (iFullChannels+1)&6, width iFullChannels
            else w.WriteBits((uint)(val + max + 1), 2 + 1);       // width iFullChannels+1
        }
        else
        {
            w.WriteBits((uint)cbp, 2);
        }
        ctx.CbpCountMax = Clamp8(countM + 1 - 4 * (cbp == max ? 1 : 0));
        ctx.CbpCountZero = Clamp8(countZ + 1 - 4 * (cbp == 0 ? 1 : 0));

        // pass 0: luma
        if (numY != 0) { lapMean[0] += numY; EncodeBlock(false, rlY, numY, ctx, CtDc, w, 1); }
        if (mbitsY != 0)
            for (var k = 1; k < 16; k++)
                w.WriteBits((uint)(residualY[k] >> 1), mbitsY + (residualY[k] & 1));

        // pass 1: joint U+V (interleaved refinement: U[k] then V[k], k = 1..3 / 1..7)
        if (numC != 0) { lapMean[1] += numC; EncodeBlock(true, rlC, numC, ctx, CtDc, w, is420 ? 10 : 2); }
        if (mbitsC != 0)
        {
            int refCount = is420 ? 4 : 8;
            for (var k = 1; k < refCount; k++)
            {
                int u = mb.BlockDc[1][k];
                w.WriteBits((uint)Math.Abs(u), mbitsC);
                if (bufU[k] == 0 && u != 0) w.WriteBit(u < 0);
                int v = mb.BlockDc[2][k];
                w.WriteBits((uint)Math.Abs(v), mbitsC);
                if (bufV[k] == 0 && v != 0) w.WriteBit(v < 0);
            }
        }

        ModelBits.UpdateMb(ctx.ColorFormat, ctx.Channels, lapMean, model);
        if (resetContext) ctx.AdaptLowpass();
    }

    // segdec.c DecodeMacroblockLowpass, YUV420/422 branch — exact mirror of EncodeLowpassChroma.
    private static void DecodeLowpassChroma(CodingContext ctx, Macroblock mb, ref BitReader r, bool resetContext)
    {
        var model = ctx.ModelLp;
        var scan = ctx.ScanLowpass;
        var lapMean = new int[2];
        bool is420 = ctx.ColorFormat == ColorFormat.Yuv420;
        int remapOff = is420 ? 1 : 0;
        int count = is420 ? 6 : 14;

        // LP-CBP (iFullPlanes = 2, iMax = 3)
        const int max = 2 * 4 - 5;
        int countM = ctx.CbpCountMax, countZ = ctx.CbpCountZero;
        int cbp;
        if (countZ <= 0 || countM < 0)
        {
            cbp = 0;
            if (r.ReadBit())
            {
                cbp = 1;
                int k = (int)r.ReadBits(2 - 1);
                if (k != 0) cbp = k * 2 + (int)r.ReadBits(1);
            }
            if (countM < countZ) cbp = max - cbp;
        }
        else cbp = (int)r.ReadBits(2);
        ctx.CbpCountMax = Clamp8(countM + 1 - 4 * (cbp == max ? 1 : 0));
        ctx.CbpCountZero = Clamp8(countZ + 1 - 4 * (cbp == 0 ? 1 : 0));

        // pass 0: luma
        int mbitsY = model.FlcBits[0];
        if ((cbp & 1) != 0)
        {
            var rl = new int[32];
            int n = DecodeBlock(false, rl, ctx, CtDc, ref r, 1);
            lapMean[0] += n;
            int idx = 1;
            for (var k = 0; k < n; k++) { idx += rl[k * 2]; mb.BlockDc[0][scan.Scan[idx]] = rl[k * 2 + 1]; scan.Visit(idx); idx++; }
        }
        if (mbitsY != 0) DecodeLpResidual(mb.BlockDc[0], ref r, mbitsY);

        // pass 1: joint U+V
        int mbitsC = model.FlcBits[1];
        if ((cbp & 2) != 0)
        {
            var rl = new int[32];
            int n = DecodeBlock(true, rl, ctx, CtDc, ref r, is420 ? 10 : 2);
            lapMean[1] += n;
            var aTemp = new int[16];
            int idx = 0;
            for (var k = 0; k < n; k++) { idx += rl[k * 2]; aTemp[idx & 0xf] = rl[k * 2 + 1]; idx++; }
            for (var k = 0; k < count; k++)
                mb.BlockDc[(k & 1) + 1][LpChromaRemap[remapOff + (k >> 1)]] = aTemp[k];
        }
        if (mbitsC != 0)
        {
            int refCount = is420 ? 4 : 8;
            for (var k = 1; k < refCount; k++)
            {
                mb.BlockDc[1][k] = RefineLpChroma(mb.BlockDc[1][k], ref r, mbitsC);
                mb.BlockDc[2][k] = RefineLpChroma(mb.BlockDc[2][k], ref r, mbitsC);
            }
        }

        ModelBits.UpdateMb(ctx.ColorFormat, ctx.Channels, lapMean, model);
        if (resetContext) ctx.AdaptLowpass();
    }

    // The 444 LP luma-residual decode (the RotateLeft refinement), reused for the luma pass of the
    // chroma LP band. Mirrors the inline block in DecodeLowpass.
    private static void DecodeLpResidual(int[] coeffs, ref BitReader r, int mbits)
    {
        int mask = (1 << mbits) - 1;
        for (var k = 1; k < 16; k++)
        {
            if (coeffs[k] != 0)
            {
                int r1 = (int)BitOperations.RotateLeft((uint)coeffs[k], mbits);
                coeffs[k] = (r1 ^ (int)r.ReadBits(mbits)) - (r1 & mask);
            }
            else
            {
                uint v = r.PeekBits(mbits + 1);
                int val = (int)((v >> 1) ^ (uint)-(int)(v & 1)) + (int)(v & 1);
                coeffs[k] = val;
                r.SkipBits(mbits + (val != 0 ? 1 : 0));
            }
        }
    }

    // segdec.c chroma LP refinement of one coefficient: <paramref name="hi"/> is the run/level high
    // part already scattered into the plane; fold in the low <paramref name="mbits"/> bits (sign in
    // the high part when present, else a trailing sign bit on a nonzero value).
    private static int RefineLpChroma(int hi, ref BitReader r, int mbits)
    {
        if (hi > 0) return (hi << mbits) + (int)r.ReadBits(mbits);
        if (hi < 0) return (hi << mbits) - (int)r.ReadBits(mbits);
        int v = (int)r.ReadBits(mbits);
        if (v != 0 && r.ReadBit()) v = -v;
        return v;
    }

    // ===================================================================== HP band

    internal static void EncodeHighpass(CodingContext ctx, Macroblock mb, BitWriter w, BitWriter fl,
                                        bool ctxLeft, bool ctxTop, int[]? leftCbp, int[]? topCbp, bool resetContext, bool resetTotals)
    {
        if (resetTotals) { ctx.ScanHoriz.Reset(); ctx.ScanVert.Reset(); } // jxrlib m_bResetRGITotals — START of HP band
        CodeCbp(ctx, mb, w, ctxLeft, ctxTop, leftCbp, topCbp);
        CodeCoeffs(ctx, mb, w, fl);
        if (resetContext) ctx.AdaptHighpass();
    }

    internal static void DecodeHighpass(CodingContext ctx, Macroblock mb, ref BitReader r, ref BitReader fl, int hpQp,
                                        bool ctxLeft, bool ctxTop, int[]? leftCbp, int[]? topCbp, bool resetContext, bool resetTotals,
                                        int[]? hpQpCh = null)
    {
        if (resetTotals) { ctx.ScanHoriz.Reset(); ctx.ScanVert.Reset(); } // jxrlib m_bResetRGITotals — START of HP band
        DecodeCbp(ctx, mb, ref r);
        // predCBPDec reconstructs the actual CBP from the transmitted residual + neighbors.
        bool subC = ctx.ColorFormat is ColorFormat.Yuv420 or ColorFormat.Yuv422;
        bool is420C = ctx.ColorFormat == ColorFormat.Yuv420;
        for (var i = 0; i < ctx.Channels; i++)
            mb.Cbp[i] = subC && i > 0
                ? CbpPrediction.PredictDecChroma(mb.DiffCbp[i], ctxLeft, ctxTop, topCbp?[i] ?? 0, leftCbp?[i] ?? 0, is420C, ctx.Cbp)
                : CbpPrediction.PredictDec(mb.DiffCbp[i], ctxLeft, ctxTop, topCbp?[i] ?? 0, leftCbp?[i] ?? 0, i, ctx.Cbp);
        DecodeCoeffs(ctx, mb, ref r, ref fl, hpQp, hpQpCh);
        if (resetContext) ctx.AdaptHighpass();
    }

    // ----------------------------------------------------------- CBP (encode)

    private static readonly int[] CbpNumOnes = { 0, 1, 1, 2, 1, 2, 2, 3, 1, 2, 2, 3, 2, 3, 3, 4 };
    private static readonly int[] CbpTabLen = { 0, 2, 2, 2, 2, 2, 3, 2, 2, 3, 3, 2, 3, 2, 2, 0 };
    private static readonly int[] CbpTabCode = { 0, 0, 1, 0, 2, 1, 4, 3, 3, 5, 6, 2, 7, 1, 0, 0 };
    private static readonly int[] CbpGTab0 = { 0, 1, 1, 2, 1, 3, 3, 4, 1, 3, 3, 4, 2, 4, 4, 5 };
    private static readonly int[] CbpGFl0 = { 0, 2, 2, 1, 2, 2, 2, 2, 2, 2, 2, 2, 1, 2, 2, 0 };
    private static readonly int[] CbpGCode0 = { 0, 0, 1, 0, 2, 0, 1, 0, 3, 2, 3, 1, 1, 2, 3, 0 };

    private static void CodeCbp(CodingContext ctx, Macroblock mb, BitWriter w,
                                bool ctxLeft, bool ctxTop, int[]? leftCbp, int[]? topCbp)
    {
        // predCBPEnc: derive the actual CBP from significant HP coefficients, then the
        // transmitted residual via the adaptive prediction model + neighbors.
        int acThreshold0 = (1 << ctx.ModelAc.FlcBits[0]) - 1;
        int acThreshold1 = (1 << ctx.ModelAc.FlcBits[1]) - 1;
        bool is420 = ctx.ColorFormat == ColorFormat.Yuv420;
        bool reduced = ctx.ColorFormat is ColorFormat.Yuv420 or ColorFormat.Yuv422; // subsampled chroma
        for (var ch = 0; ch < ctx.Channels; ch++)
        {
            int threshold = ch == 0 ? acThreshold0 : acThreshold1, threshold2 = threshold * 2 + 1;
            int blocks = ch == 0 ? 16 : MacroblockLayout.ChromaBlocks(ctx.ColorFormat);
            int[] offsets = ch == 0 ? MacroblockLayout.BlkOffset : MacroblockLayout.ChromaBlkOffset(ctx.ColorFormat);
            int cbp = 0;
            for (var j = 0; j < blocks; j++)
            {
                int off = offsets[j];
                for (var i = 1; i < 16; i++)
                    if ((uint)(mb.Plane[ch][off + i] + threshold) >= (uint)threshold2) { cbp |= 1 << j; break; }
            }
            mb.Cbp[ch] = cbp;
            mb.DiffCbp[ch] = reduced && ch > 0
                ? CbpPrediction.PredictEncChroma(cbp, ctxLeft, ctxTop, topCbp?[ch] ?? 0, leftCbp?[ch] ?? 0, is420, ctx.Cbp)
                : CbpPrediction.PredictEnc(cbp, ctxLeft, ctxTop, topCbp?[ch] ?? 0, leftCbp?[ch] ?? 0, ch, ctx.Cbp);
        }

        if (!IsYuv(ctx.ColorFormat)) { CodeCbpYOnly(ctx, mb, w); return; }
        if (reduced) { CodeCbpChromaSub(ctx, mb, w, is420); return; }

        int diffY = mb.DiffCbp[0], diffU = mb.DiffCbp[1], diffV = mb.DiffCbp[2];

        // super-block presence pattern (YUV444 ORs the chroma into luma).
        int pattern = 0, dy = diffY | (diffU | diffV);
        for (var iBlock = 0; iBlock < 4; iBlock++)
        {
            pattern |= (dy & 0xf) != 0 ? 0x10 : 0;
            dy >>= 4;
            pattern >>= 1;
        }

        int count = CbpNumOnes[pattern];
        ctx.CbpCy1.Encode(w, count);
        ctx.CbpCy1.Discriminant += ctx.CbpCy1.Delta(count);
        if (CbpTabLen[pattern] != 0) w.WriteBits((uint)CbpTabCode[pattern], CbpTabLen[pattern]);

        for (var iBlock = 0; iBlock < 4; iBlock++)
        {
            int code = diffY & 0xf;
            int codeU = diffU & 0xf, codeV = diffV & 0xf;
            code |= (codeU != 0 ? 1 : 0) << 4;
            code |= (codeV != 0 ? 1 : 0) << 5;
            diffY >>= 4; diffU >>= 4; diffV >>= 4;

            if (code != 0)
            {
                int chroma = code >> 4;
                code &= 0xf;
                int val = chroma != 0 ? (CbpGTab0[code] > 2 ? 8 : CbpGTab0[code] + 6 - 1) : CbpGTab0[code] - 1;
                ctx.CbpCy.Encode(w, val);
                ctx.CbpCy.Discriminant += ctx.CbpCy.Delta(val);

                if (chroma != 0)
                {
                    if (chroma == 1) w.WriteBits(1, 1);
                    else w.WriteBits((uint)(3 - chroma), 2);
                }
                if (val == 8)
                {
                    if (CbpGTab0[code] == 3) w.WriteBits(1, 1);
                    else w.WriteBits((uint)(5 - CbpGTab0[code]), 2);
                }
                if (CbpGFl0[code] != 0) w.WriteBits((uint)CbpGCode0[code], CbpGFl0[code]);

                // YUV444 per-block chroma sub-CBP (U then V) via AHexpt[1] (alphabet 4).
                int sub = codeU;
                for (var k = 0; k < 2; k++)
                {
                    if (sub != 0)
                    {
                        ctx.AHexpt[1].Encode(w, CbpNumOnes[sub] - 1);
                        if (CbpTabLen[sub] != 0) w.WriteBits((uint)CbpTabCode[sub], CbpTabLen[sub]);
                    }
                    sub = codeV;
                }
            }
        }
    }

    // jxrlib CodeCBP, Y_ONLY / N_CHANNEL branch: per channel, no chroma OR into the super-block
    // pattern, no chroma sub-CBP, and val = gTab0[code]-1 (never the chroma +6/==8 path). The
    // per-block CbpCy uses alphabet 5 (CodingContext seeds it from the "small" format flag).
    private static void CodeCbpYOnly(CodingContext ctx, Macroblock mb, BitWriter w)
    {
        for (var ch = 0; ch < ctx.Channels; ch++)
        {
            int diff = mb.DiffCbp[ch];

            int pattern = 0, dy = diff;
            for (var iBlock = 0; iBlock < 4; iBlock++)
            {
                pattern |= (dy & 0xf) != 0 ? 0x10 : 0;
                dy >>= 4;
                pattern >>= 1;
            }

            int count = CbpNumOnes[pattern];
            ctx.CbpCy1.Encode(w, count);
            ctx.CbpCy1.Discriminant += ctx.CbpCy1.Delta(count);
            if (CbpTabLen[pattern] != 0) w.WriteBits((uint)CbpTabCode[pattern], CbpTabLen[pattern]);

            for (var iBlock = 0; iBlock < 4; iBlock++)
            {
                int code = diff & 0xf;
                diff >>= 4;
                if (code != 0)
                {
                    int val = CbpGTab0[code] - 1;
                    ctx.CbpCy.Encode(w, val);
                    ctx.CbpCy.Discriminant += ctx.CbpCy.Delta(val);
                    if (CbpGFl0[code] != 0) w.WriteBits((uint)CbpGCode0[code], CbpGFl0[code]);
                }
            }
        }
    }

    // segenc.c CodeCBP, YUV420/422 branch. The luma DiffCBP plus the two reduced chroma DiffCBPs
    // are PackCBP-interleaved into one word of 4 super-block chunks (6 bits for 420 = 4Y+1U+1V,
    // 8 bits for 422 = 4Y+2U+2V); each chunk is then coded with the shared luma-nibble symbol
    // (CbpCy / gTab0) plus the chroma flags. The super-block presence pattern is sent first (CbpCy1).
    private static void CodeCbpChromaSub(CodingContext ctx, Macroblock mb, BitWriter w, bool is420)
    {
        int diffY = mb.DiffCbp[0], diffU = mb.DiffCbp[1], diffV = mb.DiffCbp[2];

        int packed = is420
            ? (diffY & 0xf) + ((diffU & 1) << 4) + ((diffV & 1) << 5)
              + ((diffY & 0x00f0) << 2) + ((diffU & 2) << 9) + ((diffV & 2) << 10)
              + ((diffY & 0x0f00) << 4) + ((diffU & 4) << 14) + ((diffV & 4) << 15)
              + ((diffY & 0xf000) << 6) + ((diffU & 8) << 19) + ((diffV & 8) << 20)
            : (diffY & 0xf) + ((diffU & 1) << 4) + ((diffU & 4) << 3)
              + ((diffV & 1) << 6) + ((diffV & 4) << 5)
              + ((diffY & 0x00f0) << 4) + ((diffU & 2) << 11) + ((diffU & 8) << 10)
              + ((diffV & 2) << 13) + ((diffV & 8) << 12)
              + ((diffY & 0x0f00) << 8) + ((diffU & 16) << 16) + ((diffU & 64) << 15)
              + ((diffV & 16) << 18) + ((diffV & 64) << 17)
              + ((diffY & 0xf000) << 12) + ((diffU & 32) << 23) + ((diffU & 128) << 22)
              + ((diffV & 32) << 25) + ((diffV & 128) << 24);

        int chunkBits = is420 ? 6 : 8;
        int chunkMask = is420 ? 0x3f : 0xff;

        // super-block presence pattern
        int pattern = 0, dy = packed;
        for (var iBlock = 0; iBlock < 4; iBlock++)
        {
            pattern |= (dy & chunkMask) != 0 ? 0x10 : 0;
            dy >>= chunkBits;
            pattern >>= 1;
        }

        int count = CbpNumOnes[pattern];
        ctx.CbpCy1.Encode(w, count);
        ctx.CbpCy1.Discriminant += ctx.CbpCy1.Delta(count);
        if (CbpTabLen[pattern] != 0) w.WriteBits((uint)CbpTabCode[pattern], CbpTabLen[pattern]);

        for (var iBlock = 0; iBlock < 4; iBlock++)
        {
            int code = packed & chunkMask;
            packed >>= chunkBits;
            if (code == 0) continue;

            int chroma = code >> 4;
            code &= 0xf;
            int codeU = 0, codeV = 0;
            if (!is420)
            {
                codeU = chroma & 3;
                codeV = (chroma >> 2) & 3;
                chroma = codeU == 0 ? 0 : 1;
                if (codeV != 0) chroma += 2;
            }

            int val = chroma != 0 ? (CbpGTab0[code] > 2 ? 8 : CbpGTab0[code] + 6 - 1) : CbpGTab0[code] - 1;
            ctx.CbpCy.Encode(w, val);
            ctx.CbpCy.Discriminant += ctx.CbpCy.Delta(val);

            if (chroma != 0)
            {
                if (chroma == 1) w.WriteBits(1, 1);
                else w.WriteBits((uint)(3 - chroma), 2);
            }
            if (val == 8)
            {
                if (CbpGTab0[code] == 3) w.WriteBits(1, 1);
                else w.WriteBits((uint)(5 - CbpGTab0[code]), 2);
            }
            if (CbpGFl0[code] != 0) w.WriteBits((uint)CbpGCode0[code], CbpGFl0[code]);

            // 422 carries a per-plane 2-bit chroma row pattern; 420 has none (U/V are single bits).
            if (!is420)
            {
                int patt = codeU;
                for (var k = 0; k < 2; k++)
                {
                    if (patt != 0)
                    {
                        if (patt == 1) w.WriteBits(1, 1);
                        else w.WriteBits((uint)(3 - patt), 2);
                    }
                    patt = codeV;
                }
            }
        }
    }

    // ----------------------------------------------------------- CBP (decode)

    private static readonly int[] CbpGFlc0 = { 0, 2, 1, 2, 2, 0 };
    private static readonly int[] CbpGOff0 = { 0, 4, 2, 8, 12, 1 };
    private static readonly int[] CbpGOut0 = { 0, 15, 3, 12, 1, 2, 4, 8, 5, 6, 9, 10, 7, 11, 13, 14 };

    private static readonly int[] CbpShift422 = { 0, 1, 4, 5 };

    private static void DecodeCbp(CodingContext ctx, Macroblock mb, ref BitReader r)
    {
        if (!IsYuv(ctx.ColorFormat)) { DecodeCbpYOnly(ctx, mb, ref r); return; }

        int cbpY = 0, cbpU = 0, cbpV = 0;

        int numCbp = ctx.CbpCy1.Decode(ref r);
        ctx.CbpCy1.Discriminant += ctx.CbpCy1.Delta(numCbp);
        numCbp = ExpandCbpCount(numCbp, ref r);

        for (var iBlock = 0; iBlock < 4; iBlock++)
        {
            if ((numCbp & (1 << iBlock)) == 0) continue;

            int blockCbp = ctx.CbpCy.Decode(ref r);
            ctx.CbpCy.Discriminant += ctx.CbpCy.Delta(blockCbp);
            uint val = (uint)blockCbp + 1;
            blockCbp = 0;

            if (val >= 6) // chroma present
            {
                blockCbp = r.ReadBit() ? 0x10 : (r.ReadBit() ? 0x20 : 0x30);
                if (val == 9)
                {
                    if (r.ReadBit()) { /* val stays 9 */ }
                    else if (r.ReadBit()) val = 10;
                    else val = 11;
                }
                val -= 6;
            }
            int code1 = CbpGOff0[val];
            if (CbpGFlc0[val] != 0) code1 += (int)r.ReadBits(CbpGFlc0[val]);
            blockCbp += CbpGOut0[code1];

            cbpY |= (blockCbp & 0xf) << (iBlock * 4);
            if (ctx.ColorFormat == ColorFormat.Yuv444)
            {
                for (var k = 0; k < 2; k++)
                {
                    if (((blockCbp >> (k + 4)) & 1) == 0) continue;
                    int code = ExpandChromaSubCbp(ctx.AHexpt[1].Decode(ref r), ref r);
                    if (k == 0) cbpU |= code << (iBlock * 4);
                    else cbpV |= code << (iBlock * 4);
                }
            }
            else if (ctx.ColorFormat == ColorFormat.Yuv420)
            {
                cbpU |= ((blockCbp >> 4) & 1) << iBlock;
                cbpV |= ((blockCbp >> 5) & 1) << iBlock;
            }
            else // Yuv422: a per-plane 2-bit row pattern coded 1/4/5, placed via CbpShift422
            {
                for (var k = 0; k < 2; k++)
                {
                    if (((blockCbp >> (k + 4)) & 1) == 0) continue;
                    int code = 5;
                    if (r.ReadBit()) code = 1;
                    else if (r.ReadBit()) code = 4;
                    code <<= CbpShift422[iBlock];
                    if (k == 0) cbpU |= code;
                    else cbpV |= code;
                }
            }
        }

        mb.DiffCbp[0] = cbpY;
        mb.DiffCbp[1] = cbpU;
        mb.DiffCbp[2] = cbpV;
    }

    // jxrlib DecodeCBP, Y_ONLY / N_CHANNEL branch (default case): per channel, no chroma path
    // (val is always < 6 so the >=6 chroma block never fires) and no chroma sub-CBP. Sets only
    // DiffCbp[ch]. The per-block CbpCy uses alphabet 5 (seeded by CodingContext).
    private static void DecodeCbpYOnly(CodingContext ctx, Macroblock mb, ref BitReader r)
    {
        for (var ch = 0; ch < ctx.Channels; ch++)
        {
            int cbp = 0;
            int numCbp = ctx.CbpCy1.Decode(ref r);
            ctx.CbpCy1.Discriminant += ctx.CbpCy1.Delta(numCbp);
            numCbp = ExpandCbpCount(numCbp, ref r);

            for (var iBlock = 0; iBlock < 4; iBlock++)
            {
                if ((numCbp & (1 << iBlock)) == 0) continue;
                int blockCbp = ctx.CbpCy.Decode(ref r);
                ctx.CbpCy.Discriminant += ctx.CbpCy.Delta(blockCbp);
                int val = blockCbp + 1; // Y-only: 1..5, never the chroma (>=6) path
                int code1 = CbpGOff0[val];
                if (CbpGFlc0[val] != 0) code1 += (int)r.ReadBits(CbpGFlc0[val]);
                cbp |= CbpGOut0[code1] << (iBlock * 4);
            }
            mb.DiffCbp[ch] = cbp;
        }
    }

    // The CbpCy1 count symbol expands to the 4-bit super-block pattern (mirror of CodeCBP's pattern→count).
    private static int ExpandCbpCount(int numCbp, ref BitReader r)
    {
        switch (numCbp)
        {
            case 1: return 1 << (int)r.ReadBits(2);
            case 2:
                int t = (int)r.ReadBits(2);
                if (t == 0) return 3;
                if (t == 1) return 5;
                int[] tab = { 6, 9, 10, 12 };
                return tab[t * 2 + (r.ReadBit() ? 1 : 0) - 4];
            case 3: return 0xf ^ (1 << (int)r.ReadBits(2));
            case 4: return 0xf;
            default: return numCbp; // 0 -> no blocks
        }
    }

    // The AHexpt[1] sub-CBP symbol expands to a 4-bit chroma block pattern (same shape as ExpandCbpCount).
    private static int ExpandChromaSubCbp(int code, ref BitReader r)
    {
        switch (code)
        {
            case 0: return 1 << (int)r.ReadBits(2);
            case 1:
                int t = (int)r.ReadBits(2);
                if (t == 0) return 3;
                if (t == 1) return 5;
                int[] tab = { 6, 9, 10, 12 };
                return tab[t * 2 + (r.ReadBit() ? 1 : 0) - 4];
            case 2: return 0xf ^ (1 << (int)r.ReadBits(2));
            case 3: return 0xf;
            default: return code;
        }
    }

    // ----------------------------------------------------------- HP coefficients

    private static void CodeCoeffs(CodingContext ctx, Macroblock mb, BitWriter w, BitWriter fl)
    {
        if (ctx.ColorFormat is ColorFormat.Yuv420 or ColorFormat.Yuv422)
        {
            CodeCoeffsChroma(ctx, mb, w, fl, ctx.ColorFormat == ColorFormat.Yuv420);
            return;
        }
        var scan = mb.Orientation == 1 ? ctx.ScanVert : ctx.ScanHoriz;
        var order = MacroblockLayout.DctIndex; // flexbits emit/read order within a block
        int trim = ctx.TrimFlexBits;           // 0 in the default profile
        int mbits = ctx.ModelAc.FlcBits[0];
        int flex = ctx.NoFlexBits ? 0 : (mbits >= trim ? mbits - trim : 0);
        int mask = (1 << flex) - 1;
        var lapMean = new int[2];
        var localCoef = new int[32];
        var residual = new int[16];
        bool chroma = false;

        for (var i = 0; i < ctx.Channels; i++)
        {
            int pattern = mb.Cbp[i];
            for (var iBlock = 0; iBlock < 4; iBlock++)
            {
                for (var sub = 0; sub < 4; sub++, pattern >>= 1)
                {
                    int iIndex = iBlock * 4 + sub;
                    var coeffs = mb.Plane[i];
                    int off = MacroblockLayout.BlkOffset[iIndex];
                    if ((pattern & 1) == 0)
                    {
                        // not significant: the whole (sub-threshold) coefficients ride the flexbits.
                        if (flex > 0)
                            for (var k = 1; k < 16; k++)
                            {
                                int data = coeffs[off + order[k]];
                                int atdata = Math.Abs(data) >> trim;
                                int word = atdata & mask, len = flex;
                                if (atdata != 0) { word += word + (data < 0 ? 1 : 0); len++; }
                                fl.WriteBits((uint)word, len);
                            }
                    }
                    else
                    {
                        int n = mbits == 0
                            ? ScanZero(coeffs, off, scan, localCoef)
                            : ScanCoefficients(coeffs, off, residual, scan, mbits, localCoef, trim);
                        lapMean[chroma ? 1 : 0] += n;
                        EncodeBlock(chroma, localCoef, n, ctx, CtHp, w, 1);
                        if (flex > 0)
                            for (var k = 1; k < 16; k++)
                            {
                                int p = order[k];
                                fl.WriteBits((uint)(residual[p] >> 1), flex + (residual[p] & 1));
                            }
                    }
                }
                if (iBlock == 3)
                {
                    mbits = ctx.ModelAc.FlcBits[1]; chroma = true;
                    flex = ctx.NoFlexBits ? 0 : (mbits >= trim ? mbits - trim : 0);
                    mask = (1 << flex) - 1;
                }
            }
        }

        ModelBits.UpdateMb(ctx.ColorFormat, ctx.Channels, lapMean, ctx.ModelAc);
    }

    private static void DecodeCoeffs(CodingContext ctx, Macroblock mb, ref BitReader r, ref BitReader fl, int hpQp, int[]? hpQpCh = null)
    {
        if (ctx.ColorFormat is ColorFormat.Yuv420 or ColorFormat.Yuv422)
        {
            DecodeCoeffsChroma(ctx, mb, ref r, ref fl, hpQp, ctx.ColorFormat == ColorFormat.Yuv420, hpQpCh);
            return;
        }
        var scan = mb.Orientation == 1 ? ctx.ScanVert : ctx.ScanHoriz;
        int trim = ctx.TrimFlexBits;
        int mbits = ctx.ModelAc.FlcBits[0];
        var lapMean = new int[2];
        bool chroma = false;
        int cbp = mb.Cbp[0];

        for (var i = 0; i < ctx.Channels; i++)
        {
            int chHp = hpQpCh?[i] ?? hpQp; // per-channel HP step (quality mode); else the shared step
            for (var iBlock = 0; iBlock < 4; iBlock++)
            {
                for (var sub = 0; sub < 4; sub++, cbp >>= 1)
                {
                    int iIndex = iBlock * 4 + sub;
                    var coeffs = mb.Plane[i];
                    int off = MacroblockLayout.BlkOffset[iIndex];
                    int n = DecodeBlockAdaptive((cbp & 1) != 0, chroma, ctx, ref r, ref fl, coeffs, off, scan, mbits, trim, chHp);
                    lapMean[chroma ? 1 : 0] += n;
                }
                if (iBlock == 3) { mbits = ctx.ModelAc.FlcBits[1]; chroma = true; }
            }
            if (i + 1 < ctx.Channels) cbp = mb.Cbp[i + 1]; // jxrlib iCBP[(i+1)&0xf]; final read is unused
        }

        ModelBits.UpdateMb(ctx.ColorFormat, ctx.Channels, lapMean, ctx.ModelAc);
    }

    // segenc.c EncodeMacroblockHighpass, YUV420/422 branch: a single pass over iNBlocks super-blocks
    // (6 = 4Y+1U+1V for 420, 8 = 4Y+2U+2V for 422) driven by the combined CBP pattern
    // (luma 16 bits + chroma U/V packed at <<16 / <<20|24). Luma blocks index Plane[0]/BlkOffset;
    // chroma blocks index the reduced Plane[1]/[2] via BlkOffsetUV. The luma→chroma model switch is
    // after iBlock==3, exactly as 444.
    private static void CodeCoeffsChroma(CodingContext ctx, Macroblock mb, BitWriter w, BitWriter fl, bool is420)
    {
        var scan = mb.Orientation == 1 ? ctx.ScanVert : ctx.ScanHoriz;
        var order = MacroblockLayout.DctIndex;
        int trim = ctx.TrimFlexBits;
        int mbits = ctx.ModelAc.FlcBits[0];
        int flex = ctx.NoFlexBits ? 0 : (mbits >= trim ? mbits - trim : 0);
        int mask = (1 << flex) - 1;
        var lapMean = new int[2];
        var localCoef = new int[32];
        var residual = new int[16];
        bool chroma = false;

        int iNBlocks = is420 ? 6 : 8;
        int pattern = mb.Cbp[0] + (mb.Cbp[1] << 16) + (mb.Cbp[2] << (is420 ? 20 : 24));
        var off420 = MacroblockLayout.BlkOffsetUV420;
        var off422 = MacroblockLayout.BlkOffsetUV422;

        for (var iBlock = 0; iBlock < iNBlocks; iBlock++)
        {
            for (var sub = 0; sub < 4; sub++, pattern >>= 1)
            {
                int[] coeffs;
                int off;
                if (iBlock < 4) { coeffs = mb.Plane[0]; off = MacroblockLayout.BlkOffset[iBlock * 4 + sub]; }
                else if (is420) { coeffs = mb.Plane[iBlock - 3]; off = off420[sub]; }
                else { coeffs = mb.Plane[1 + ((iBlock - 4) >> 1)]; off = off422[(iBlock & 1) * 4 + sub]; }

                if ((pattern & 1) == 0)
                {
                    if (flex > 0)
                        for (var k = 1; k < 16; k++)
                        {
                            int data = coeffs[off + order[k]];
                            int atdata = Math.Abs(data) >> trim;
                            int word = atdata & mask, len = flex;
                            if (atdata != 0) { word += word + (data < 0 ? 1 : 0); len++; }
                            fl.WriteBits((uint)word, len);
                        }
                }
                else
                {
                    int n = mbits == 0
                        ? ScanZero(coeffs, off, scan, localCoef)
                        : ScanCoefficients(coeffs, off, residual, scan, mbits, localCoef, trim);
                    lapMean[chroma ? 1 : 0] += n;
                    EncodeBlock(chroma, localCoef, n, ctx, CtHp, w, 1);
                    if (flex > 0)
                        for (var k = 1; k < 16; k++)
                        {
                            int p = order[k];
                            fl.WriteBits((uint)(residual[p] >> 1), flex + (residual[p] & 1));
                        }
                }
            }
            if (iBlock == 3)
            {
                mbits = ctx.ModelAc.FlcBits[1]; chroma = true;
                flex = ctx.NoFlexBits ? 0 : (mbits >= trim ? mbits - trim : 0);
                mask = (1 << flex) - 1;
            }
        }

        ModelBits.UpdateMb(ctx.ColorFormat, ctx.Channels, lapMean, ctx.ModelAc);
    }

    // segdec.c DecodeMacroblockHighpass, YUV420/422 branch — exact mirror of CodeCoeffsChroma.
    private static void DecodeCoeffsChroma(CodingContext ctx, Macroblock mb, ref BitReader r, ref BitReader fl, int hpQp, bool is420, int[]? hpQpCh = null)
    {
        var scan = mb.Orientation == 1 ? ctx.ScanVert : ctx.ScanHoriz;
        int trim = ctx.TrimFlexBits;
        int mbits = ctx.ModelAc.FlcBits[0];
        var lapMean = new int[2];
        bool chroma = false;

        int iNBlocks = is420 ? 6 : 8;
        int pattern = mb.Cbp[0] + (mb.Cbp[1] << 16) + (mb.Cbp[2] << (is420 ? 20 : 24));
        var off420 = MacroblockLayout.BlkOffsetUV420;
        var off422 = MacroblockLayout.BlkOffsetUV422;

        for (var iBlock = 0; iBlock < iNBlocks; iBlock++)
        {
            for (var sub = 0; sub < 4; sub++, pattern >>= 1)
            {
                int[] coeffs;
                int off;
                int chIdx;
                if (iBlock < 4) { coeffs = mb.Plane[0]; off = MacroblockLayout.BlkOffset[iBlock * 4 + sub]; chIdx = 0; }
                else if (is420) { chIdx = iBlock - 3; coeffs = mb.Plane[chIdx]; off = off420[sub]; }
                else { chIdx = 1 + ((iBlock - 4) >> 1); coeffs = mb.Plane[chIdx]; off = off422[(iBlock & 1) * 4 + sub]; }

                int n = DecodeBlockAdaptive((pattern & 1) != 0, chroma, ctx, ref r, ref fl, coeffs, off, scan, mbits, trim, hpQpCh?[chIdx] ?? hpQp);
                lapMean[chroma ? 1 : 0] += n;
            }
            if (iBlock == 3) { mbits = ctx.ModelAc.FlcBits[1]; chroma = true; }
        }

        ModelBits.UpdateMb(ctx.ColorFormat, ctx.Channels, lapMean, ctx.ModelAc);
    }

    // segdec.c DecodeBlockAdaptive — decode the highpass "high" part (run/level, dequantized by
    // iQP<<mbits) and then fold in the flexbits "low" part. Mirrors CodeCoeffs' significant /
    // non-significant split.
    private static int DecodeBlockAdaptive(bool noSkip, bool chroma, CodingContext ctx,
                                           ref BitReader r, ref BitReader fl, int[] coeffs, int off,
                                           AdaptiveScan scan, int mbits, int trim, int qp)
    {
        int n = 0;
        int flex = ctx.NoFlexBits ? 0 : mbits - trim;
        if (flex < 0) flex = 0;

        if (noSkip)
            n = DecodeBlockHighpass(chroma, ctx, ref r, qp << mbits, coeffs, off, scan);

        if (flex > 0)
        {
            var order = MacroblockLayout.DctIndex;
            if (qp + trim == 1) // lossless HP (trim == 0, qp == 1)
            {
                for (var k = 1; k < 16; k++)
                {
                    int pi = off + order[k];
                    if (coeffs[pi] < 0) coeffs[pi] -= (int)fl.ReadBits(flex);
                    else if (coeffs[pi] > 0) coeffs[pi] += (int)fl.ReadBits(flex);
                    else coeffs[pi] = GetBit16s(ref fl, flex);
                }
            }
            else
            {
                int qp1 = qp << trim;
                for (var k = 1; k < 16; k++)
                {
                    int pi = off + order[k];
                    if (coeffs[pi] < 0) coeffs[pi] -= qp1 * (int)fl.ReadBits(flex);
                    else if (coeffs[pi] > 0) coeffs[pi] += qp1 * (int)fl.ReadBits(flex);
                    else coeffs[pi] = qp1 * GetBit16s(ref fl, flex);
                }
            }
        }
        return n;
    }

    // segdec.c _getBit16s — read cBits+1 bits as a signed value (sign in the LSB); a zero value
    // consumes only cBits.
    private static int GetBit16s(ref BitReader r, int cBits)
    {
        uint v = r.PeekBits(cBits + 1);
        int ret = (int)((v >> 1) ^ (uint)-(int)(v & 1)) + (int)(v & 1);
        r.SkipBits(cBits + (ret != 0 ? 1 : 0));
        return ret;
    }

    // ----------------------------------------------------------- adaptive scans

    // segenc.c AdaptiveScan non-significant residual code (the gRes LUT value, computed for any
    // model-bits width — the LUT only covers small levels / mbits<6; this matches its formula
    // with iTrim=0): 0 for level 0, 4*level+1 for level>0, 4*|level|+3 for level<0.
    private static int Residual(int level)
    {
        int sign = -(level < 0 ? 1 : 0); // 0 or -1
        return (level ^ sign) * 4 + (6 & sign) + (level != 0 ? 1 : 0);
    }

    // segenc.c AdaptiveScanZero — model bits = 0 (the first-MB highpass case): pure run/level, no residual.
    private static int ScanZero(int[] coeffs, int off, AdaptiveScan scan, int[] rl)
    {
        int run = 1, n = 0;
        int level = coeffs[off + scan.Scan[1]];
        if (level != 0) { scan.Visit(1); rl[0] = 0; rl[1] = level; n = 1; run = 0; }
        for (var k = 2; k < 16; k++)
        {
            level = coeffs[off + scan.Scan[k]];
            run++;
            if (level != 0)
            {
                scan.Visit(k);
                rl[n * 2] = run - 1; rl[n * 2 + 1] = level; n++; run = 0;
            }
        }
        return n;
    }

    // segenc.c AdaptiveScan, iTrim==0 branch (any model-bits width): splits each coefficient at
    // block base <paramref name="off"/> into a run/level "high" part and a per-coefficient
    // residual "low" part (residual is within-block indexed, 0..15). Used by both LP (off=0,
    // coeffs=BlockDc) and HP (off=blkOffset, coeffs=plane).
    // <paramref name="trim"/> is TRIM_FLEXBITS: the low <c>trim</c> bits of every coefficient's
    // flexbits refinement are dropped (jxrlib iTrimBits), folded into the residual exactly as
    // segenc.c scanACEnc — the run-level high part is unaffected, only the flexbits residual.
    private static int ScanCoefficients(int[] coeffs, int off, int[] residual, AdaptiveScan scan, int mbits, int[] rl, int trim = 0)
    {
        int thOff = (1 << mbits) - 1, th = thOff * 2 + 1;
        int run = 0, n = 0;

        int s1 = scan.Scan[1], level = coeffs[off + s1];
        if ((uint)(level + thOff) >= (uint)th)
        {
            int abs = Math.Abs(level), hi = abs >> mbits;
            residual[s1] = ((abs & thOff) >> trim) * 2;
            scan.Visit(1);
            rl[0] = run; rl[1] = level < 0 ? -hi : hi; n = 1; run = 0;
        }
        else { run++; residual[s1] = Residual(TrimLevel(level, trim)); }

        for (var k = 2; k < 16; k++)
        {
            int sk = scan.Scan[k];
            level = coeffs[off + sk];
            if ((uint)(level + thOff) >= (uint)th)
            {
                int sign = -(level < 0 ? 1 : 0);
                int abs = (sign ^ level) - sign, hi = abs >> mbits;
                residual[sk] = ((abs & thOff) >> trim) * 2;
                scan.Visit(k);
                rl[n * 2] = run; rl[n * 2 + 1] = (hi ^ sign) - sign; n++; run = 0;
            }
            else { run++; residual[sk] = Residual(TrimLevel(level, trim)); }
        }
        return n;
    }

    // segenc.c: round a sub-threshold level toward zero by TRIM_FLEXBITS before packing the
    // flexbits residual — iTemp = -(level<0); ((level + iTemp) >> trim) - iTemp.
    private static int TrimLevel(int level, int trim)
    {
        int temp = -(level < 0 ? 1 : 0);
        return ((level + temp) >> trim) - temp;
    }

    // ----------------------------------------------------------- block coders (context-pool)

    private static readonly int[] EobCode = { 0, 6, 2, 7 };
    private static readonly int[] EobLen = { 1, 3, 2, 3 };

    private static void EncodeFirstIndex(bool chroma, int index, bool sign, CodingContext ctx, int offset, BitWriter w)
    {
        var ah = ctx.AHexpt[offset + (chroma ? 3 : 0)];
        ah.Discriminant += ah.Delta(index);
        ah.Discriminant1 += ah.Delta1(index);
        var (code, len) = ah.Code(index);
        w.WriteBits((uint)(code * 2 + (sign ? 1 : 0)), len + 1);
    }

    private static void EncodeIndex(bool chroma, int loc, int cont, int index, bool sign, CodingContext ctx, int offset, BitWriter w)
    {
        if (loc < 15)
        {
            var ah = ctx.AHexpt[offset + cont + 1 + (chroma ? 3 : 0)];
            ah.Discriminant += ah.Delta(index);
            ah.Discriminant1 += ah.Delta1(index);
            var (code, len) = ah.Code(index);
            w.WriteBits((uint)(code * 2 + (sign ? 1 : 0)), len + 1);
        }
        else if (loc == 15)
        {
            w.WriteBits((uint)(EobCode[index] * 2 + (sign ? 1 : 0)), EobLen[index] + 1);
        }
        else
        {
            w.WriteBits((uint)(index * 2 + (sign ? 1 : 0)), 2);
        }
    }

    private static void EncodeBlock(bool chroma, int[] coef, int n, CodingContext ctx, int offset, BitWriter w, int loc)
    {
        int lev = coef[1];
        int sr = coef[0] == 0 ? 1 : 0;
        int sl = (uint)(lev + 1) > 2u ? 1 : 0;
        int srn = n == 1 ? 0 : (coef[2] > 0 ? 2 : 1);

        EncodeFirstIndex(chroma, srn * 4 + sl * 2 + sr, lev < 0, ctx, offset, w);
        int cont = sr & srn;
        if (sl != 0) CoefficientSyntax.EncodeAbsLevel(w, ctx.AHexpt[6 + offset + cont], Math.Abs(lev));
        if (sr == 0) CoefficientSyntax.EncodeRun(w, ctx.AHexpt[0], coef[0], 15 - loc);
        loc += coef[0] + 1;

        for (var k = 1; k < n; k++)
        {
            if (srn == 2) CoefficientSyntax.EncodeRun(w, ctx.AHexpt[0], coef[k * 2], 15 - loc);
            loc += coef[k * 2] + 1;
            srn = k == n - 1 ? 0 : (coef[k * 2 + 2] > 0 ? 2 : 1);
            lev = coef[k * 2 + 1];
            sl = (uint)(lev + 1) > 2u ? 1 : 0;
            EncodeIndex(chroma, loc, cont, srn * 2 + sl, lev < 0, ctx, offset, w);
            cont &= srn;
            if (sl != 0) CoefficientSyntax.EncodeAbsLevel(w, ctx.AHexpt[6 + offset + cont], Math.Abs(lev));
        }
    }

    private static int DecodeFirstIndex(AdaptiveHuffman ah, ref BitReader r)
    {
        int index = ah.Decode(ref r);
        ah.Discriminant += ah.Delta(index);
        ah.Discriminant1 += ah.Delta1(index);
        return index;
    }

    private static int DecodeIndex(int loc, AdaptiveHuffman ah, ref BitReader r)
    {
        if (loc < 15)
        {
            int index = ah.Decode(ref r);
            ah.Discriminant += ah.Delta(index);
            ah.Discriminant1 += ah.Delta1(index);
            return index;
        }
        if (loc == 15)
        {
            if (!r.ReadBit()) return 0;
            if (!r.ReadBit()) return 2;
            return 1 + 2 * (r.ReadBit() ? 1 : 0);
        }
        return r.ReadBit() ? 1 : 0;
    }

    // segdec.c DecodeBlock — lowpass run/level pairs into aRLCoeffs.
    private static int DecodeBlock(bool chroma, int[] coef, CodingContext ctx, int offset, ref BitReader r, int loc)
    {
        int baseCtx = offset + (chroma ? 3 : 0);
        int index = DecodeFirstIndex(ctx.AHexpt[baseCtx], ref r);
        int sr = index & 1, srn = index >> 2;
        int cont = sr & srn;
        int sign = r.ReadBit() ? -1 : 0;

        coef[1] = (index & 2) != 0
            ? (CoefficientSyntax.DecodeAbsLevel(ref r, ctx.AHexpt[6 + offset + cont]) ^ sign) - sign
            : 1 | sign;
        coef[0] = sr == 0 ? CoefficientSyntax.DecodeRun(ref r, ctx.AHexpt[0], 15 - loc) : 0;
        loc += coef[0] + 1;

        int n = 1;
        while (srn != 0)
        {
            sr = srn & 1;
            coef[n * 2] = sr == 0 ? CoefficientSyntax.DecodeRun(ref r, ctx.AHexpt[0], 15 - loc) : 0;
            loc += coef[n * 2] + 1;
            index = DecodeIndex(loc, ctx.AHexpt[baseCtx + cont + 1], ref r);
            srn = index >> 1;
            cont &= srn;
            sign = r.ReadBit() ? -1 : 0;
            coef[n * 2 + 1] = (index & 1) != 0
                ? (CoefficientSyntax.DecodeAbsLevel(ref r, ctx.AHexpt[6 + offset + cont]) ^ sign) - sign
                : 1 | sign;
            n++;
        }
        return n;
    }

    // segdec.c DecodeBlockHighpass — writes dequantized highpass coefficients straight into the plane.
    private static int DecodeBlockHighpass(bool chroma, CodingContext ctx, ref BitReader r, int qp, int[] coef, int off, AdaptiveScan scan)
    {
        int baseCtx = CtHp + (chroma ? 3 : 0);
        int loc = 1;

        int index = DecodeFirstIndex(ctx.AHexpt[baseCtx], ref r);
        int sr = index & 1, srn = index >> 2;
        int cont = sr & srn;
        int sign = r.ReadBit() ? -1 : 0;

        int level = (qp ^ sign) - sign;
        if ((index & 2) != 0) level *= CoefficientSyntax.DecodeAbsLevel(ref r, ctx.AHexpt[6 + CtHp + cont]);
        if (sr == 0) loc += CoefficientSyntax.DecodeRun(ref r, ctx.AHexpt[0], 15 - loc);
        loc &= 0xf;
        coef[off + scan.Scan[loc]] = level;
        scan.Visit(loc);
        loc = (loc + 1) & 0xf;

        int n = 1;
        while (srn != 0)
        {
            sr = srn & 1;
            if (sr == 0)
            {
                loc += CoefficientSyntax.DecodeRun(ref r, ctx.AHexpt[0], 15 - loc);
                if (loc >= 16) return 16;
            }
            index = DecodeIndex(loc + 1, ctx.AHexpt[baseCtx + cont + 1], ref r);
            srn = index >> 1;
            cont &= srn;
            sign = r.ReadBit() ? -1 : 0;

            level = (qp ^ sign) - sign;
            if ((index & 1) != 0) level *= CoefficientSyntax.DecodeAbsLevel(ref r, ctx.AHexpt[6 + CtHp + cont]);
            coef[off + scan.Scan[loc]] = level;
            scan.Visit(loc);
            loc = (loc + 1) & 0xf;
            n++;
        }
        return n;
    }

    private static int Clamp8(int x) => x < -8 ? -8 : x > 7 ? 7 : x;
}
