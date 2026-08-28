namespace SharpAstro.Jxr;

/// <summary>
/// The per-macroblock <b>signal path</b> for BD8 RGB → YUV444, <c>OL_NONE</c> (no
/// overlap). Bridges 16×16 spatial pixels and a quantized coefficient
/// <see cref="Macroblock"/>: forward = color transform (<c>_CC</c>) → load into the
/// block-layout plane (via <c>idxCC</c>) → 2-stage Photo Core Transform → quantize →
/// extract the DC block; inverse reverses it. With no overlap each macroblock is
/// self-contained (no cross-MB staging — that's the pipelined OL_ONE/OL_TWO path).
///
/// <para>Ports the BD8 RGB input/output (jxrlib strenc.c <c>_CC</c> + idxCC store
/// <c>pU=-r, pV=b, pY=g-128</c>, strdec.c <c>_ICC</c>), the 2-stage transform
/// (<see cref="PhotoCoreTransform"/>), quantization (<see cref="Quantization"/>),
/// and the DC-block extract/restore (<c>dctIndex[2]</c>, <c>dequantizeMacroblock</c>).
/// At QP index 0 (lossless) the whole path is bit-exact pixel identity.</para>
/// </summary>
internal static class SignalTransform
{
    private const int Bias = 128; // BD8 luma level shift (jxrlib iOffset, cShift=0)

    // common.h SHIFTZERO=1, QPFRACBITS=2. In scaled-arithmetic mode jxrlib pre-scales the transform
    // input by cShift = SHIFTZERO+QPFRACBITS (==3) on encode and shifts the output back down by the
    // same amount on decode (strdec.c outputMBRow). YUV420/422 always run scaled (the chroma
    // resolution change disqualifies the lossless fast-path), so their decode needs this output shift.
    public const int ShiftZero = 1;
    public const int QpFracBits = 2;
    public const int ScaledShift = ShiftZero + QpFracBits; // 3

    // strcodec.c dctIndex[2] — DC-block (super-DC) layout: BlockDc[i] <-> plane[DcIndex[i]].
    private static readonly int[] DcIndex =
        { 0, 128, 64, 208, 32, 240, 48, 224, 16, 192, 80, 144, 112, 176, 96, 160 };

    // strcodec.c idxCC[row][col] flattened to [row*16 + col] — pixel position -> plane position.
    // Internal so the chroma upsampler (ChromaUpsample) can address the full-resolution plane.
    internal static readonly int[] IdxCc = BuildIdxCc();

    private static int[] BuildIdxCc()
    {
        // The 16x16 table from jxrlib; high nibble = block-major, low nibble = within-block (4x4 zigzag-ish).
        ReadOnlySpan<byte> rows = stackalloc byte[]
        {
            0x00,0x01,0x05,0x04, 0x40,0x41,0x45,0x44, 0x80,0x81,0x85,0x84, 0xc0,0xc1,0xc5,0xc4,
            0x02,0x03,0x07,0x06, 0x42,0x43,0x47,0x46, 0x82,0x83,0x87,0x86, 0xc2,0xc3,0xc7,0xc6,
            0x0a,0x0b,0x0f,0x0e, 0x4a,0x4b,0x4f,0x4e, 0x8a,0x8b,0x8f,0x8e, 0xca,0xcb,0xcf,0xce,
            0x08,0x09,0x0d,0x0c, 0x48,0x49,0x4d,0x4c, 0x88,0x89,0x8d,0x8c, 0xc8,0xc9,0xcd,0xcc,
            0x10,0x11,0x15,0x14, 0x50,0x51,0x55,0x54, 0x90,0x91,0x95,0x94, 0xd0,0xd1,0xd5,0xd4,
            0x12,0x13,0x17,0x16, 0x52,0x53,0x57,0x56, 0x92,0x93,0x97,0x96, 0xd2,0xd3,0xd7,0xd6,
            0x1a,0x1b,0x1f,0x1e, 0x5a,0x5b,0x5f,0x5e, 0x9a,0x9b,0x9f,0x9e, 0xda,0xdb,0xdf,0xde,
            0x18,0x19,0x1d,0x1c, 0x58,0x59,0x5d,0x5c, 0x98,0x99,0x9d,0x9c, 0xd8,0xd9,0xdd,0xdc,
            0x20,0x21,0x25,0x24, 0x60,0x61,0x65,0x64, 0xa0,0xa1,0xa5,0xa4, 0xe0,0xe1,0xe5,0xe4,
            0x22,0x23,0x27,0x26, 0x62,0x63,0x67,0x66, 0xa2,0xa3,0xa7,0xa6, 0xe2,0xe3,0xe7,0xe6,
            0x2a,0x2b,0x2f,0x2e, 0x6a,0x6b,0x6f,0x6e, 0xaa,0xab,0xaf,0xae, 0xea,0xeb,0xef,0xee,
            0x28,0x29,0x2d,0x2c, 0x68,0x69,0x6d,0x6c, 0xa8,0xa9,0xad,0xac, 0xe8,0xe9,0xed,0xec,
            0x30,0x31,0x35,0x34, 0x70,0x71,0x75,0x74, 0xb0,0xb1,0xb5,0xb4, 0xf0,0xf1,0xf5,0xf4,
            0x32,0x33,0x37,0x36, 0x72,0x73,0x77,0x76, 0xb2,0xb3,0xb7,0xb6, 0xf2,0xf3,0xf7,0xf6,
            0x3a,0x3b,0x3f,0x3e, 0x7a,0x7b,0x7f,0x7e, 0xba,0xbb,0xbf,0xbe, 0xfa,0xfb,0xff,0xfe,
            0x38,0x39,0x3d,0x3c, 0x78,0x79,0x7d,0x7c, 0xb8,0xb9,0xbd,0xbc, 0xf8,0xf9,0xfd,0xfc,
        };
        var t = new int[256];
        for (var i = 0; i < 256; i++) t[i] = rows[i];
        return t;
    }

    // ---------------------------------------------------------------- forward

    /// <summary>
    /// Forward signal path for one BD8 RGB macroblock (each channel 256 samples in
    /// raster order, pixel (row,col) at row*16+col) into a quantized YUV444
    /// <see cref="Macroblock"/> (<see cref="Macroblock.BlockDc"/> = quantized DC+LP,
    /// <see cref="Macroblock.Plane"/> = quantized coefficients incl. HP).
    /// </summary>
    public static void Forward(ReadOnlySpan<int> r, ReadOnlySpan<int> g, ReadOnlySpan<int> b, Macroblock mb,
                               in JxrQuantizer qDc, in JxrQuantizer qLp, in JxrQuantizer qHp)
    {
        var pY = mb.Plane[0];
        var pU = mb.Plane[1];
        var pV = mb.Plane[2];

        // color transform (_CC) + load into the block-layout plane.
        for (var px = 0; px < 256; px++)
        {
            int rr = r[px], gg = g[px], bb = b[px];
            ColorTransform.ForwardRgb(ref rr, ref gg, ref bb);
            int pos = IdxCc[px];
            pU[pos] = -rr;
            pV[pos] = bb;
            pY[pos] = gg - Bias;
        }

        for (var ch = 0; ch < 3; ch++)
        {
            var p = mb.Plane[ch];
            // 2-stage forward Photo Core Transform.
            for (var blk = 0; blk < 16; blk++)
                PhotoCoreTransform.ForwardStage1(p.AsSpan(16 * blk, 16));
            PhotoCoreTransform.ForwardStage2(p);

            // quantize (DC at block 0, LP at the other 15 super-DC slots, HP everywhere else).
            for (var j = 0; j < 16; j++)
            {
                int off = MacroblockLayout.BlkOffset[j];
                p[off] = Quantization.Quantize(p[off], j == 0 ? qDc : qLp);
                for (var i = 1; i < 16; i++)
                    p[off + i] = Quantization.Quantize(p[off + i], qHp);
            }

            // extract the DC block (quantized DC + 15 LP) for the DC/LP entropy bands.
            for (var i = 0; i < 16; i++)
                mb.BlockDc[ch][i] = p[DcIndex[i]];
        }
    }

    /// <summary>
    /// Dequantize the highpass coefficients in place (the step the entropy decoder
    /// folds into <c>DecodeBlockHighpass</c>). Call this in signal-path isolation
    /// tests between <see cref="Forward"/> and <see cref="Inverse"/>; in the full
    /// codec the entropy decode already produces dequantized HP.
    /// </summary>
    public static void DequantizeHighpass(Macroblock mb, int hpQp)
    {
        for (var ch = 0; ch < 3; ch++)
            for (var j = 0; j < 16; j++)
            {
                int off = MacroblockLayout.BlkOffset[j];
                for (var i = 1; i < 16; i++)
                    mb.Plane[ch][off + i] = Quantization.Dequantize(mb.Plane[ch][off + i], hpQp);
            }
    }

    // ---------------------------------------------------------------- inverse

    /// <summary>
    /// Inverse signal path: dequantize DC/LP from <see cref="Macroblock.BlockDc"/>
    /// into the plane (jxrlib <c>dequantizeMacroblock</c>; HP is assumed already
    /// dequantized), run the inverse 2-stage transform, then unload + inverse color
    /// (<c>_ICC</c>) into the BD8 RGB output (256 samples per channel, raster order).
    /// </summary>
    public static void Inverse(Macroblock mb, Span<int> r, Span<int> g, Span<int> b,
                               int dcQp, int lpQp)
    {
        for (var ch = 0; ch < 3; ch++)
        {
            var p = mb.Plane[ch];
            // restore dequantized DC + LP from the DC block.
            p[DcIndex[0]] = Quantization.Dequantize(mb.BlockDc[ch][0], dcQp);
            for (var i = 1; i < 16; i++)
                p[DcIndex[i]] = Quantization.Dequantize(mb.BlockDc[ch][i], lpQp);

            // inverse 2-stage transform.
            PhotoCoreTransform.InverseStage2(p);
            for (var blk = 0; blk < 16; blk++)
                PhotoCoreTransform.InverseStage1(p.AsSpan(16 * blk, 16));
        }

        var pY = mb.Plane[0];
        var pU = mb.Plane[1];
        var pV = mb.Plane[2];
        for (var px = 0; px < 256; px++)
        {
            int pos = IdxCc[px];
            int gg = pY[pos] + Bias, rr = -pU[pos], bb = pV[pos];
            ColorTransform.InverseRgb(ref rr, ref gg, ref bb);
            r[px] = rr; g[px] = gg; b[px] = bb;
        }
    }

    // ---------------------------------------------------------------- split path
    // The whole-image overlap pipeline (OverlapTransform) needs the color/quant
    // halves of Forward/Inverse split apart from the transform, which it owns and
    // runs across the MB grid. These four helpers are the same operations, working
    // on a per-channel whole-image plane buffer at the macroblock base offset.

    /// <summary>Color transform (<c>_CC</c>) + idxCC load of one RGB macroblock into the three
    /// whole-image YUV planes at <paramref name="mbBase"/> (no transform). Forward half 1.
    /// <paramref name="bias"/> is the luma level shift (128 for BD8, 32768 for BD16).</summary>
    public static void LoadColor(ReadOnlySpan<int> r, ReadOnlySpan<int> g, ReadOnlySpan<int> b,
                                 int[] planeY, int[] planeU, int[] planeV, int mbBase, int bias = Bias, int shift = 0)
    {
        // jxrlib inputMBRow CF_RGB BD8: r/g/b are scaled <<cShift BEFORE _CC, iOffset = 128<<cShift
        // (cShift = SHIFTZERO+QPFRACBITS = 3 in scaled-arith mode, 0 otherwise). The colour transform
        // has rounding lifts, so the shift must precede it — it is not a post-multiply.
        int iOffset = bias << shift;
        for (var px = 0; px < 256; px++)
        {
            int rr = r[px] << shift, gg = g[px] << shift, bb = b[px] << shift;
            ColorTransform.ForwardRgb(ref rr, ref gg, ref bb);
            int pos = mbBase + IdxCc[px];
            planeU[pos] = -rr;
            planeV[pos] = bb;
            planeY[pos] = gg - iOffset;
        }
    }

    /// <summary>Quantize the already-transformed coefficients of one MB (read from
    /// <paramref name="plane"/> at <paramref name="mbBase"/>) into <see cref="Macroblock.Plane"/>
    /// and extract the DC block. Forward half 2.</summary>
    public static void QuantizeExtract(ReadOnlySpan<int> plane, int mbBase, Macroblock mb, int ch,
                                       in JxrQuantizer qDc, in JxrQuantizer qLp, in JxrQuantizer qHp)
    {
        var p = mb.Plane[ch];
        plane.Slice(mbBase, 256).CopyTo(p);
        for (var j = 0; j < 16; j++)
        {
            int off = MacroblockLayout.BlkOffset[j];
            p[off] = Quantization.Quantize(p[off], j == 0 ? qDc : qLp);
            for (var i = 1; i < 16; i++)
                p[off + i] = Quantization.Quantize(p[off + i], qHp);
        }
        for (var i = 0; i < 16; i++)
            mb.BlockDc[ch][i] = p[DcIndex[i]];
    }

    /// <summary>
    /// Reduced-chroma counterpart of <see cref="QuantizeExtract"/> for YUV420/422: quantize the
    /// transformed reduced-chroma MB (read from <paramref name="plane"/>, stride 64/128, at
    /// <paramref name="mbBase"/>) into <see cref="Macroblock.Plane"/> and extract the per-block DCs.
    /// The exact inverse of <see cref="DequantizeRestoreChroma"/>: block 0's DC uses the DC quantizer,
    /// the other blocks' DCs use the LP quantizer, and every block's 15 AC coefficients use the HP
    /// quantizer (jxrlib <c>quantizeMacroblock</c>, 420/422 branch).
    /// </summary>
    public static void QuantizeExtractChroma(ReadOnlySpan<int> plane, int mbBase, Macroblock mb, int ch,
                                             in JxrQuantizer qDc, in JxrQuantizer qLp, in JxrQuantizer qHp, ColorFormat cf)
    {
        int blocks = MacroblockLayout.ChromaBlocks(cf);
        int stride = blocks * 16;
        var off = MacroblockLayout.ChromaBlkOffset(cf);
        var p = mb.Plane[ch];
        plane.Slice(mbBase, stride).CopyTo(p);
        for (var j = 0; j < blocks; j++)
        {
            int o = off[j];
            p[o] = Quantization.Quantize(p[o], j == 0 ? qDc : qLp);
            for (var i = 1; i < 16; i++)
                p[o + i] = Quantization.Quantize(p[o + i], qHp);
        }
        for (var i = 0; i < blocks; i++)
            mb.BlockDc[ch][i] = p[off[i]];
    }

    /// <summary>Write one decoded MB's dequantized coefficients (HP from
    /// <see cref="Macroblock.Plane"/>, DC/LP dequantized from <see cref="Macroblock.BlockDc"/>)
    /// into <paramref name="plane"/> at <paramref name="mbBase"/>. Inverse half 1.</summary>
    public static void DequantizeRestore(Macroblock mb, int ch, int[] plane, int mbBase, int dcQp, int lpQp)
    {
        // HP is already dequantized by the entropy decoder and sits in mb.Plane[ch].
        mb.Plane[ch].AsSpan(0, 256).CopyTo(plane.AsSpan(mbBase, 256));
        plane[mbBase + DcIndex[0]] = Quantization.Dequantize(mb.BlockDc[ch][0], dcQp);
        for (var i = 1; i < 16; i++)
            plane[mbBase + DcIndex[i]] = Quantization.Dequantize(mb.BlockDc[ch][i], lpQp);
    }

    /// <summary>
    /// Reduced-chroma counterpart of <see cref="DequantizeRestore"/> for YUV420/422: write one
    /// decoded chroma MB into the reduced whole-image plane (stride 64/128) at <paramref name="mbBase"/>.
    /// HP is already dequantized in <see cref="Macroblock.Plane"/>; the DC + LP from
    /// <see cref="Macroblock.BlockDc"/> dequantize to the block-DC positions
    /// (<see cref="MacroblockLayout.ChromaBlkOffset"/>, jxrlib <c>dequantizeBlock2x2/4x2</c>:
    /// BlockDc[i] → plane[blkOffsetUV[i]]).
    /// </summary>
    public static void DequantizeRestoreChroma(Macroblock mb, int ch, int[] plane, int mbBase, int dcQp, int lpQp, ColorFormat cf)
    {
        int blocks = MacroblockLayout.ChromaBlocks(cf);
        int stride = blocks * 16;
        var off = MacroblockLayout.ChromaBlkOffset(cf);
        mb.Plane[ch].AsSpan(0, stride).CopyTo(plane.AsSpan(mbBase, stride));
        plane[mbBase + off[0]] = Quantization.Dequantize(mb.BlockDc[ch][0], dcQp);
        for (var i = 1; i < blocks; i++)
            plane[mbBase + off[i]] = Quantization.Dequantize(mb.BlockDc[ch][i], lpQp);
    }

    /// <summary>Inverse color (<c>_ICC</c>) + idxCC unload of one MB from the three whole-image
    /// YUV planes at <paramref name="mbBase"/> into RGB samples, clamped to <c>[0, <paramref name="max"/>]</c>
    /// (jxrlib's output _CLIP). <paramref name="bias"/> = 128/32768 for BD8/BD16. Inverse half 2.
    ///
    /// <para><paramref name="shift"/> is jxrlib's scaled-arithmetic output shift (0 for lossless 444,
    /// <see cref="ScaledShift"/>=3 for the always-scaled YUV420/422 path). It mirrors strdec.c
    /// outputMBRow: the level bias is added to Y, the inverse colour transform runs, then all three
    /// channels are right-shifted by <paramref name="shift"/> (so the encode-side <c>&lt;&lt;cShift</c> input
    /// scaling is undone). The rounding bias <c>(1&lt;&lt;(shift-1))-1</c> matches jxrlib's iBias2.</para></summary>
    public static void StoreColor(int[] planeY, int[] planeU, int[] planeV, int mbBase,
                                  Span<int> r, Span<int> g, Span<int> b, int bias = Bias, int max = 255, int shift = 0,
                                  bool bd16Round = false, int min = 0)
    {
        // jxrlib iBias = (level << iShift) + iBias2. For BD8 (strdec.c:989) iBias2 = scaled ?
        // (1<<(iShift-1))-1 : 0; BD16-integer (strdec.c:1183) uses (1<<(iShift-1)) — the same half but
        // WITHOUT the -1. (The level <<nLen post-shift is a no-op at our nLen=0.) Only differs when
        // scaled (shift>0), so BD16 lossless is unaffected. Signed BD16S/BD32S use level 0 + _CLIP16 /
        // no clip (min..max). BD16S uses the BD8-style round (-1, strdec.c:1342), so bd16Round stays false.
        int round = shift > 0 ? (1 << (shift - 1)) - (bd16Round ? 0 : 1) : 0;
        int iBias = (bias << shift) + round;
        for (var px = 0; px < 256; px++)
        {
            int pos = mbBase + IdxCc[px];
            int gg = planeY[pos] + iBias, rr = -planeU[pos], bb = planeV[pos];
            ColorTransform.InverseRgb(ref rr, ref gg, ref bb);
            r[px] = Clip(rr >> shift, min, max); g[px] = Clip(gg >> shift, min, max); b[px] = Clip(bb >> shift, min, max);
        }
    }

    private static int Clip(int v, int min, int max) => v < min ? min : v > max ? max : v;

    // ---------------------------------------------------------------- grayscale (Y-only)
    // jxrlib BD8 Y_ONLY input does NO color transform (strenc.c:1921-1938): the single
    // channel becomes the Y plane via idxCC with the level shift `pY = src - (128 << cShift)`
    // (cShift = 0 for BD8). Inverse adds the bias back.

    /// <summary>idxCC load of one grayscale macroblock (256 samples, raster order) into the
    /// whole-image Y plane at <paramref name="mbBase"/> with the level shift. No color transform.
    /// <paramref name="bias"/> = 128/32768 for BD8/BD16.</summary>
    public static void LoadGray(ReadOnlySpan<int> y, int[] planeY, int mbBase, int bias = Bias, int shift = 0)
    {
        // Scaled-arith (lossy QP) scales the sample <<cShift before the level shift, exactly like
        // the RGB luma load: pY = (src << cShift) - (bias << cShift) = (src - bias) << cShift.
        for (var px = 0; px < 256; px++)
            planeY[mbBase + IdxCc[px]] = (y[px] - bias) << shift;
    }

    /// <summary>idxCC unload of one MB from the whole-image Y plane at <paramref name="mbBase"/>
    /// back into grayscale samples, adding the bias and clamping to <c>[0, <paramref name="max"/>]</c>.
    /// No color transform. <paramref name="bias"/> = 128/32768 for BD8/BD16.</summary>
    public static void StoreGray(int[] planeY, int mbBase, Span<int> y, int bias = Bias, int max = 255, int shift = 0,
                                 bool bd16Round = false, int min = 0)
    {
        // jxrlib iBias = (bias << iShift) + round, then >> iShift (same level/rounding as luma): BD8 uses
        // round = (1<<(iShift-1))-1, BD16-integer uses (1<<(iShift-1)) (no -1). Signed BD16S/BD32S use
        // level 0, the BD8-style round (-1), and a signed clip (min..max). Only differs when scaled.
        int round = shift > 0 ? (1 << (shift - 1)) - (bd16Round ? 0 : 1) : 0;
        int iBias = (bias << shift) + round;
        for (var px = 0; px < 256; px++)
            y[px] = Clip((planeY[mbBase + IdxCc[px]] + iBias) >> shift, min, max);
    }

    // ---------------------------------------------------------------- grayscale float (BD32F/BD16F)
    // jxrlib float input does NO color transform and NO luma bias (strenc.c:2284): the float is
    // mapped straight to the internal PixelI via float2pixel, which is already signed/centered.

    /// <summary>idxCC load of one BD32F grayscale macroblock (256 floats, raster order) into the
    /// whole-image Y plane at <paramref name="mbBase"/> via <see cref="FloatPixel.ToPixel"/>.</summary>
    public static void LoadGrayFloat(ReadOnlySpan<float> y, int[] planeY, int mbBase, int expBias, int lenMantissa)
    {
        for (var px = 0; px < 256; px++)
            planeY[mbBase + IdxCc[px]] = FloatPixel.ToPixel(y[px], expBias, lenMantissa);
    }

    /// <summary>idxCC unload of one MB from the whole-image Y plane at <paramref name="mbBase"/>
    /// back into BD32F grayscale floats via <see cref="FloatPixel.ToFloat"/>.</summary>
    public static void StoreGrayFloat(int[] planeY, int mbBase, Span<float> y, int expBias, int lenMantissa)
    {
        for (var px = 0; px < 256; px++)
            y[px] = FloatPixel.ToFloat(planeY[mbBase + IdxCc[px]], expBias, lenMantissa);
    }

    // ---------------------------------------------------------------- half (BD16F)
    // jxrlib keeps the half as its raw sign-magnitude bits (forwardHalf/backwardHalf) — no bias,
    // and for RGB the YCoCg-R color transform runs on those reinterpreted integers (reversibly).

    /// <summary>idxCC load of one BD16F grayscale macroblock (256 halves) into the Y plane via
    /// <see cref="FloatPixel.HalfToPixel"/>. No color transform, no bias. In scaled-arith mode
    /// (<paramref name="shift"/>=3, lossy QP) the pixel is scaled <c>&lt;&lt;cShift</c>.</summary>
    public static void LoadGrayHalf(ReadOnlySpan<Half> y, int[] planeY, int mbBase, int shift = 0)
    {
        for (var px = 0; px < 256; px++)
            planeY[mbBase + IdxCc[px]] = FloatPixel.HalfToPixel(y[px]) << shift;
    }

    /// <summary>idxCC unload of one MB from the Y plane back into BD16F halves via
    /// <see cref="FloatPixel.PixelToHalf"/>. In scaled-arith mode adds the rounding bias
    /// <c>(1&lt;&lt;(shift-1))-1</c> (no level bias) then right-shifts by <paramref name="shift"/>.</summary>
    public static void StoreGrayHalf(int[] planeY, int mbBase, Span<Half> y, int shift = 0)
    {
        int iBias = shift > 0 ? (1 << (shift - 1)) - 1 : 0;
        for (var px = 0; px < 256; px++)
            y[px] = FloatPixel.PixelToHalf((planeY[mbBase + IdxCc[px]] + iBias) >> shift);
    }

    /// <summary>HalfToPixel + color transform (<c>_CC</c>) + idxCC load of one BD16F RGB macroblock
    /// (256 halves per channel) into the three YUV planes. No bias.</summary>
    public static void LoadColorHalf(ReadOnlySpan<Half> r, ReadOnlySpan<Half> g, ReadOnlySpan<Half> b,
                                     int[] planeY, int[] planeU, int[] planeV, int mbBase, int shift = 0)
    {
        // BD16F has no level bias (the float-pixel is signed, centred at 0); scaled-arith just scales
        // the pixel <<cShift before _CC (jxrlib inputMBRow: forwardHalf(half) << cShift).
        for (var px = 0; px < 256; px++)
        {
            int rr = FloatPixel.HalfToPixel(r[px]) << shift, gg = FloatPixel.HalfToPixel(g[px]) << shift, bb = FloatPixel.HalfToPixel(b[px]) << shift;
            ColorTransform.ForwardRgb(ref rr, ref gg, ref bb);
            int pos = mbBase + IdxCc[px];
            planeU[pos] = -rr;
            planeV[pos] = bb;
            planeY[pos] = gg;
        }
    }

    /// <summary>Inverse color (<c>_ICC</c>) + idxCC unload of one MB into BD16F RGB halves via
    /// <see cref="FloatPixel.PixelToHalf"/>. No clamp (the half encoding is bit-exact). In scaled-arith
    /// mode (<paramref name="shift"/>=3) jxrlib adds the rounding bias <c>(1&lt;&lt;(shift-1))-1</c> to Y
    /// (no level bias) before <c>_ICC</c> and right-shifts the result by <paramref name="shift"/>.</summary>
    public static void StoreColorHalf(int[] planeY, int[] planeU, int[] planeV, int mbBase,
                                      Span<Half> r, Span<Half> g, Span<Half> b, int shift = 0)
    {
        int iBias = shift > 0 ? (1 << (shift - 1)) - 1 : 0;
        for (var px = 0; px < 256; px++)
        {
            int pos = mbBase + IdxCc[px];
            int gg = planeY[pos] + iBias, rr = -planeU[pos], bb = planeV[pos];
            ColorTransform.InverseRgb(ref rr, ref gg, ref bb);
            r[px] = FloatPixel.PixelToHalf(rr >> shift); g[px] = FloatPixel.PixelToHalf(gg >> shift); b[px] = FloatPixel.PixelToHalf(bb >> shift);
        }
    }
}
