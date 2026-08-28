namespace SharpAstro.Jxr;

/// <summary>
/// The reduced-resolution chroma Photo Core Transform for YUV420 / YUV422, at
/// <c>OL_NONE</c> (no overlap). Each chroma macroblock has only 4 (420, 8×8 px) or
/// 8 (422, 8×16 px) 4×4 blocks, laid out at <see cref="MacroblockLayout.BlkOffsetUV420"/>
/// / <see cref="MacroblockLayout.BlkOffsetUV422"/> within a 64- / 128-int plane.
///
/// <para>Forward = stage-1 PCT on each block, then the chroma second stage on the block
/// DCs; inverse reverses it. With no overlap each MB is self-contained (no cross-MB
/// dependency — jxrlib's staggered two-row window produces the identical per-MB result),
/// so this mirrors <see cref="SignalTransform"/>'s per-MB 444 path. The reduced-grid Photo
/// Overlap (POT) post-filters for <c>OL_ONE</c> / <c>OL_TWO</c> decode live in
/// <see cref="ChromaOverlapTransform"/>. Faithful to jxrlib's <c>420_UV</c> / <c>422_UV</c>
/// transform loops (strFwdTransform.c / strInvTransform.c).</para>
/// </summary>
internal static class ChromaTransform
{
    private static int[] Offsets(ColorFormat cf) =>
        cf == ColorFormat.Yuv420 ? MacroblockLayout.BlkOffsetUV420 : MacroblockLayout.BlkOffsetUV422;

    /// <summary>
    /// Forward transform (encode) one chroma macroblock at <paramref name="mbBase"/> in
    /// <paramref name="plane"/> (reduced stride 64 / 128): stage-1 PCT per block, then the
    /// chroma second stage on the block DCs.
    /// </summary>
    public static void ForwardMbNoOverlap(int[] plane, int mbBase, ColorFormat cf)
    {
        var mb = plane.AsSpan(mbBase, ChromaUpsample.ReducedStride(cf));
        foreach (var off in Offsets(cf))
            PhotoCoreTransform.ForwardStage1(mb.Slice(off, 16));

        if (cf == ColorFormat.Yuv420) PhotoCoreTransform.ChromaForwardStage2_420(mb);
        else PhotoCoreTransform.ChromaForwardStage2_422(mb);
    }

    /// <summary>
    /// Inverse transform (decode) one chroma macroblock at <paramref name="mbBase"/>: the chroma
    /// second stage on the block DCs, then the stage-1 inverse PCT per block. Leaves the reduced
    /// chroma in the <c>idxCC_420</c> / <c>idxCC</c> block-scrambled spatial layout that
    /// <see cref="ChromaUpsample"/> then upsamples.
    /// </summary>
    public static void InverseMbNoOverlap(int[] plane, int mbBase, ColorFormat cf)
    {
        var mb = plane.AsSpan(mbBase, ChromaUpsample.ReducedStride(cf));
        if (cf == ColorFormat.Yuv420) PhotoCoreTransform.ChromaInverseStage2_420(mb);
        else PhotoCoreTransform.ChromaInverseStage2_422(mb);

        foreach (var off in Offsets(cf))
            PhotoCoreTransform.InverseStage1(mb.Slice(off, 16));
    }
}
