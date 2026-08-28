namespace SharpAstro.Jxr;

/// <summary>
/// The fixed permutation tables that map the 16×16 macroblock's coefficient plane
/// to block/scan order, transcribed from jxrlib (image/sys/strcodec.c and image.c).
/// A YUV444 plane is 256 <see cref="int"/>s = 16 4×4 blocks of 16 coefficients;
/// <see cref="BlkOffset"/> locates each block, <see cref="DctIndex"/> permutes the
/// 16 coefficients within a block, and the zigzag orders drive the adaptive scans.
/// </summary>
internal static class MacroblockLayout
{
    /// <summary>Number of coefficients in one macroblock plane (16 blocks × 16).</summary>
    public const int PlaneSize = 256;

    // strcodec.c — block base offsets into the 256-int plane (HP block order).
    public static readonly int[] BlkOffset =
        { 0, 64, 16, 80, 128, 192, 144, 208, 32, 96, 48, 112, 160, 224, 176, 240 };

    // strcodec.c blkOffsetUV[4] / blkOffsetUV_422[8] — chroma HP block base offsets for the
    // subsampled formats. YUV420 has 4 chroma blocks per plane (8×8 px), YUV422 has 8 (8×16 px);
    // YUV444 chroma uses the full 16-entry BlkOffset above.
    public static readonly int[] BlkOffsetUV420 = { 0, 32, 16, 48 };
    public static readonly int[] BlkOffsetUV422 = { 0, 64, 16, 80, 32, 96, 48, 112 };

    /// <summary>
    /// Number of 4×4 chroma blocks per chroma plane in a macroblock (common.h <c>cblkChromas</c>):
    /// 0 (Y-only), 4 (YUV420 = 8×8 px), 8 (YUV422 = 8×16 px), 16 (YUV444 = full 16×16 px).
    /// </summary>
    public static int ChromaBlocks(ColorFormat cf) => cf switch
    {
        ColorFormat.YOnly => 0,
        ColorFormat.Yuv420 => 4,
        ColorFormat.Yuv422 => 8,
        _ => 16,
    };

    /// <summary>HP block base offsets for a chroma plane in the given color format.</summary>
    public static int[] ChromaBlkOffset(ColorFormat cf) => cf switch
    {
        ColorFormat.Yuv420 => BlkOffsetUV420,
        ColorFormat.Yuv422 => BlkOffsetUV422,
        _ => BlkOffset,
    };

    // strcodec.c dctIndex[0] (AC 444): the within-block coefficient permutation.
    public static readonly int[] DctIndex =
        { 0, 5, 1, 6, 10, 12, 8, 14, 2, 4, 3, 7, 9, 13, 11, 15 };

    // image.c grgiZigzagInv4x4_lowpass — the LP (second-stage) scan order.
    public static readonly int[] ZigzagLowpass =
        { 0, 1, 4, 5, 2, 8, 6, 9, 3, 12, 10, 7, 13, 11, 14, 15 };

    // image.c grgiZigzagInv4x4H / V — pre-permutation zigzags for the HP scans.
    private static readonly int[] ZigzagH =
        { 0, 1, 4, 5, 2, 8, 6, 9, 3, 12, 10, 7, 13, 11, 14, 15 };
    private static readonly int[] ZigzagV =
        { 0, 4, 8, 5, 1, 12, 9, 6, 2, 13, 3, 15, 7, 10, 14, 11 };

    /// <summary>HP horizontal scan = <c>dctIndex[0][zigzagH[i]]</c> (InitZigzagScan).</summary>
    public static readonly int[] ScanHoriz = Permute(ZigzagH);

    /// <summary>HP vertical scan = <c>dctIndex[0][zigzagV[i]]</c> (InitZigzagScan).</summary>
    public static readonly int[] ScanVert = Permute(ZigzagV);

    private static int[] Permute(int[] zigzag)
    {
        var scan = new int[16];
        for (var i = 0; i < 16; i++)
            scan[i] = DctIndex[zigzag[i]];
        return scan;
    }
}
