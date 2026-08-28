namespace SharpAstro.Jxr;

/// <summary>
/// A full (single-tile) <b>OL_NONE</b> YUV444 BD8 RGB codec: ties the signal path
/// (<see cref="SignalTransform"/>) to the frequency-domain tile coder
/// (<see cref="TileCoder"/> + <see cref="MacroblockCoder"/>) over a grid of
/// macroblocks. This is the "Rung 7c" assembly — a working codec end-to-end,
/// minus the overlap filter and the container:
/// <code>
///   encode: per MB  pixels -> SignalTransform.Forward -> TileCoder.EncodeMacroblock -> bitstream
///   decode: per MB  bitstream -> TileCoder.DecodeMacroblock -> SignalTransform.Inverse -> pixels
/// </code>
/// The DC/LP/AC bands go to three separate streams (the SPATIAL codestream
/// multiplexing is a container concern for the next rung). Image dimensions must be
/// multiples of 16 (edge padding is added with the container). At QP index 0 the
/// codec is lossless.
/// </summary>
internal static class TileImageCodec
{
    /// <summary>The four band bitstreams produced by <see cref="Encode"/> (DC, LP, AC, flexbits).</summary>
    public readonly record struct Streams(byte[] Dc, byte[] Lp, byte[] Ac, byte[] Fl);

    /// <summary>
    /// Encode a <paramref name="width"/>×<paramref name="height"/> BD8 RGB image
    /// (each channel <c>width*height</c> samples in raster order) at the given QP
    /// indices (0 = lossless). Width and height must be multiples of 16.
    /// </summary>
    public static Streams Encode(ReadOnlySpan<int> r, ReadOnlySpan<int> g, ReadOnlySpan<int> b,
                                 int width, int height, int qpDc = 0, int qpLp = 0, int qpHp = 0)
    {
        RequireMbAligned(width, height);
        int mbCols = width / 16, mbRows = height / 16;
        var (qDc, qLp, qHp) = Quantizers(qpDc, qpLp, qpHp);

        var ctx = new CodingContext(ColorFormat.Yuv444, 3);
        var tile = new TileCoder(mbCols);
        var dc = new BitWriter();
        var lp = new BitWriter();
        var ac = new BitWriter();
        var fl = new BitWriter();

        var (mr, mg, mb) = (new int[256], new int[256], new int[256]);
        for (var mbR = 0; mbR < mbRows; mbR++)
        {
            for (var mbC = 0; mbC < mbCols; mbC++)
            {
                ExtractMb(r, g, b, width, mbR, mbC, mr, mg, mb);
                var block = new Macroblock(3);
                SignalTransform.Forward(mr, mg, mb, block, qDc, qLp, qHp);
                tile.EncodeMacroblock(ctx, block, mbC, mbR, dc, lp, ac, fl);
            }
            tile.AdvanceRow();
        }

        // pad each stream so the decoder's 5-bit Huffman root peeks never run off the end.
        dc.WriteBits(0, 24); lp.WriteBits(0, 24); ac.WriteBits(0, 24); fl.WriteBits(0, 24);
        return new Streams(dc.ToArray(), lp.ToArray(), ac.ToArray(), fl.ToArray());
    }

    /// <summary>Decode the band streams back into a BD8 RGB image (raster order per channel).</summary>
    public static void Decode(Streams streams, int width, int height,
                              Span<int> r, Span<int> g, Span<int> b,
                              int qpDc = 0, int qpLp = 0, int qpHp = 0)
    {
        RequireMbAligned(width, height);
        int mbCols = width / 16, mbRows = height / 16;
        var (qDc, qLp, qHp) = Quantizers(qpDc, qpLp, qpHp);

        var ctx = new CodingContext(ColorFormat.Yuv444, 3);
        var tile = new TileCoder(mbCols);
        var rdc = new BitReader(streams.Dc);
        var rlp = new BitReader(streams.Lp);
        var rac = new BitReader(streams.Ac);
        var rfl = new BitReader(streams.Fl);

        var (mr, mg, mb) = (new int[256], new int[256], new int[256]);
        for (var mbR = 0; mbR < mbRows; mbR++)
        {
            for (var mbC = 0; mbC < mbCols; mbC++)
            {
                var block = new Macroblock(3);
                tile.DecodeMacroblock(ctx, block, mbC, mbR, ref rdc, ref rlp, ref rac, ref rfl, qHp.Qp);
                SignalTransform.Inverse(block, mr, mg, mb, qDc.Qp, qLp.Qp);
                StoreMb(r, g, b, width, mbR, mbC, mr, mg, mb);
            }
            tile.AdvanceRow();
        }
    }

    // ---------------------------------------------------------------- helpers

    private static (JxrQuantizer dc, JxrQuantizer lp, JxrQuantizer hp) Quantizers(int qpDc, int qpLp, int qpHp)
        => (Quantization.Resolve(qpDc), Quantization.Resolve(qpLp), Quantization.Resolve(qpHp));

    private static void RequireMbAligned(int width, int height)
    {
        if (width <= 0 || height <= 0 || (width & 15) != 0 || (height & 15) != 0)
            throw new ArgumentException("TileImageCodec requires width and height that are positive multiples of 16.");
    }

    private static void ExtractMb(ReadOnlySpan<int> r, ReadOnlySpan<int> g, ReadOnlySpan<int> b, int width,
                                  int mbR, int mbC, int[] mr, int[] mg, int[] mb)
    {
        for (var row = 0; row < 16; row++)
        {
            int src = (mbR * 16 + row) * width + mbC * 16;
            int dst = row * 16;
            for (var col = 0; col < 16; col++)
            {
                mr[dst + col] = r[src + col];
                mg[dst + col] = g[src + col];
                mb[dst + col] = b[src + col];
            }
        }
    }

    private static void StoreMb(Span<int> r, Span<int> g, Span<int> b, int width,
                                int mbR, int mbC, int[] mr, int[] mg, int[] mb)
    {
        for (var row = 0; row < 16; row++)
        {
            int dst = (mbR * 16 + row) * width + mbC * 16;
            int src = row * 16;
            for (var col = 0; col < 16; col++)
            {
                r[dst + col] = mr[src + col];
                g[dst + col] = mg[src + col];
                b[dst + col] = mb[src + col];
            }
        }
    }
}
