namespace SharpAstro.Jxr;

/// <summary>
/// One macroblock's coefficient state — the C# analogue of jxrlib's per-MB slice
/// of <c>CWMImageStrCodec</c> (<c>MBInfo</c> + <c>pPlane[]</c>). For each channel:
/// <see cref="BlockDc"/>[ch][0] is the MB DC, [1..n-1] the second-stage lowpass (AD)
/// coefficients, and <see cref="Plane"/>[ch] is the highpass coefficient plane
/// addressed via <see cref="MacroblockLayout.BlkOffset"/> (chroma uses
/// <see cref="MacroblockLayout.ChromaBlkOffset"/>). <see cref="Cbp"/> /
/// <see cref="DiffCbp"/> hold the HP coded-block pattern and its prediction residual.
///
/// <para>The luma plane (channel 0) is always 16 blocks (256 ints). Chroma planes
/// (channels 1..) carry <c>chromaBlocks</c> 4×4 blocks: 16 for YUV444 (256 ints),
/// 8 for YUV422 (8×16 px, 128 ints), 4 for YUV420 (8×8 px, 64 ints) — jxrlib's
/// reduced-resolution internal chroma. <see cref="BlockDc"/>[ch] holds one super-DC
/// slot per block, so its length matches that channel's block count.</para>
/// </summary>
internal sealed class Macroblock
{
    public readonly int Channels;
    public readonly int[][] Plane;    // [ch][blocks*16] highpass coefficients
    public readonly int[][] BlockDc;  // [ch][blocks] DC (0) + lowpass AD (1..)
    public readonly int[] Cbp;        // [ch] highpass coded-block pattern (16 bits)
    public readonly int[] DiffCbp;    // [ch] CBP prediction residual (transmitted)

    /// <summary>0 selects the horizontal HP scan, 1 the vertical (jxrlib <c>iOrientation</c>).</summary>
    public int Orientation;

    /// <summary>YUV444 / Y-only macroblock: every channel is a full 16-block plane.</summary>
    public Macroblock(int channels) : this(channels, 16) { }

    /// <summary>
    /// Macroblock with reduced-resolution chroma: luma (channel 0) is 16 blocks, each
    /// chroma channel (1..) is <paramref name="chromaBlocks"/> blocks (16/8/4 for
    /// YUV444/422/420). Use <see cref="MacroblockLayout.ChromaBlocks"/> to derive it.
    /// </summary>
    public Macroblock(int channels, int chromaBlocks)
    {
        Channels = channels;
        Plane = new int[channels][];
        BlockDc = new int[channels][];
        Cbp = new int[channels];
        DiffCbp = new int[channels];
        for (var c = 0; c < channels; c++)
        {
            int blocks = c == 0 ? 16 : chromaBlocks;
            Plane[c] = new int[blocks * 16];
            BlockDc[c] = new int[blocks];
        }
    }
}
