using static SharpAstro.Jxr.PhotoOverlapTransform;

namespace SharpAstro.Jxr;

/// <summary>
/// The whole-image transform driver for <b>YUV444</b>: runs the two-stage Photo
/// Core Transform (and, once wired, the Photo Overlap pre-/post-filter) across a
/// grid of macroblocks, reproducing jxrlib's sliding two-MB-row window exactly.
///
/// <para>jxrlib processes one MB row at a time through a 2-row strip
/// (<c>p0</c> = the row above, <c>p1</c> = the current row; column stride 256,
/// <c>p0 = p1 - rowStride</c>) with a <b>one-MB lag</b> in both axes: the transform
/// at processing position <c>(cColumn, cRow)</c> finalizes the macroblock at
/// <c>(cColumn-1, cRow-1)</c> (strenc.c <c>processMacroblock</c>), and a Term pass
/// flushes the trailing row/column. We get the identical byte-for-byte result with
/// a simpler model: a <b>whole-image</b> coefficient buffer per channel (block-major,
/// the same column stride 256 so <c>p0 = p1 - rowStride</c>), driven over the same
/// extended grid (<c>cRow ∈ [0, mbRows], cCol ∈ [0, mbCols]</c>) with the same edge
/// flags. Because the overlap/transform at a position only reads the current row and
/// the row above (never below), pre-loading every row changes nothing — each position
/// touches the same buffer cells in the same order as jxrlib's strip.</para>
///
/// <para>Faithful port of the YUV444 branch of:
/// <list type="bullet">
/// <item>encode/strFwdTransform.c <c>transformMacroblock</c> (forward),</item>
/// <item>decode/strInvTransform.c <c>invTransformMacroblock_alteredOperators_hard</c>
/// (inverse — the exact-inverse "_alternate" operators a standard subversion-1
/// codestream selects).</item>
/// </list>
/// Single-tile only, so every tile-boundary predicate
/// (<c>bVertTileBoundary</c>/<c>bHoriTileBoundary</c>/<c>bOneMB*</c>) is constant
/// FALSE and drops out, leaving just the top/bottom/left/right edge flags.</para>
/// </summary>
internal static class OverlapTransform
{
    private const int Mb = 256; // one macroblock plane (16×16) — also the column stride

    /// <summary>Allocate the <paramref name="channels"/> whole-image channel buffers (block-major,
    /// with the one-MB right/bottom slack the boundary processing positions index into).</summary>
    public static int[][] AllocatePlanes(int mbCols, int mbRows, int channels = 3)
    {
        int len = BufferLength(mbCols, mbRows);
        var planes = new int[channels][];
        for (var c = 0; c < channels; c++) planes[c] = new int[len];
        return planes;
    }

    /// <summary>Byte offset (in ints) of macroblock <paramref name="mbC"/>,<paramref name="mbR"/> in a channel buffer.</summary>
    public static int MbBase(int mbCols, int mbR, int mbC) => mbR * RowStride(mbCols) + mbC * Mb;

    private static int RowStride(int mbCols) => (mbCols + 1) * Mb;        // +1 column of slack
    private static int BufferLength(int mbCols, int mbRows) => (mbRows + 1) * RowStride(mbCols); // +1 row of slack

    // ===================================================================== forward

    /// <summary>
    /// Forward transform every macroblock in <paramref name="planes"/> in place.
    /// On return each channel buffer holds the finalized (pre-quantization)
    /// coefficients in block-major layout, ready for per-MB quantize + entropy code.
    /// </summary>
    public static void Forward(int[][] planes, int mbCols, int mbRows, int overlap, bool scaledArith)
    {
        int rowStride = RowStride(mbCols);
        for (var cRow = 0; cRow <= mbRows; cRow++)
        {
            for (var cCol = 0; cCol <= mbCols; cCol++)
            {
                bool left = cCol == 0, right = cCol == mbCols, top = cRow == 0, bottom = cRow == mbRows;
                int p1 = cRow * rowStride + cCol * Mb;
                int p0 = p1 - rowStride;
                for (var ch = 0; ch < planes.Length; ch++)
                    ForwardMb(planes[ch], p0, p1, left, right, top, bottom, overlap, scaledArith, ch != 0);
            }
        }
    }

    private static void ForwardMb(int[] b, int p0, int p1, bool left, bool right, bool top, bool bottom,
                                  int overlap, bool scaledArith, bool chroma)
    {
        var buf = b.AsSpan();

        // ---- first level overlap (OL_ONE / OL_TWO) ----
        if (overlap != 0)
            FirstLevelOverlapEnc(buf, p0, p1, left, right, top, bottom);

        // ---- first level transform (strDCT4x4Stage1, staggered across the MB window) ----
        if (!top)
        {
            for (var j = left ? 48 : -16; j < (right ? 48 : 240); j += 64)
                PhotoCoreTransform.ForwardStage1(buf.Slice(p0 + j, 16));
        }
        if (!bottom)
        {
            for (var j = left ? 0 : -64; j < (right ? 0 : 192); j += 64)
            {
                PhotoCoreTransform.ForwardStage1(buf.Slice(p1 + j + 0, 16));
                PhotoCoreTransform.ForwardStage1(buf.Slice(p1 + j + 16, 16));
                PhotoCoreTransform.ForwardStage1(buf.Slice(p1 + j + 32, 16));
            }
        }

        // ---- second level overlap (OL_TWO) ----
        if (overlap == 2)
            SecondLevelOverlapEnc(buf, p0, p1, left, right, top, bottom);

        // ---- second level transform (strDCT4x4SecondStage on the lagged MB at p0-256) ----
        if (!(top || left))
        {
            if (scaledArith) PhotoCoreTransform.NormalizeEnc(buf.Slice(p0 - Mb, Mb), chroma);
            PhotoCoreTransform.ForwardStage2(buf.Slice(p0 - Mb, Mb));
        }
    }

    // ===================================================================== inverse

    /// <summary>
    /// Inverse transform every macroblock in <paramref name="planes"/> in place.
    /// Input is the dequantized coefficients (block-major); on return each channel
    /// buffer holds the reconstructed post-color-transform samples.
    /// </summary>
    public static void Inverse(int[][] planes, int mbCols, int mbRows, int overlap, bool scaledArith)
    {
        int rowStride = RowStride(mbCols);
        for (var cRow = 0; cRow <= mbRows; cRow++)
        {
            for (var cCol = 0; cCol <= mbCols; cCol++)
            {
                bool left = cCol == 0, right = cCol == mbCols, top = cRow == 0, bottom = cRow == mbRows;
                int p1 = cRow * rowStride + cCol * Mb;
                int p0 = p1 - rowStride;
                for (var ch = 0; ch < planes.Length; ch++)
                    InverseMb(planes[ch], p0, p1, left, right, top, bottom, overlap, scaledArith, ch != 0);
            }
        }
    }

    private static void InverseMb(int[] b, int p0, int p1, bool left, bool right, bool top, bool bottom,
                                  int overlap, bool scaledArith, bool chroma)
    {
        var buf = b.AsSpan();

        // ---- second level inverse transform (strIDCT4x4Stage2 on p1) ----
        if (!(bottom || right))
        {
            PhotoCoreTransform.InverseStage2(buf.Slice(p1, Mb));
            if (scaledArith) PhotoCoreTransform.NormalizeDec(buf.Slice(p1, Mb), chroma);
        }

        // ---- second level inverse overlap (OL_TWO) ----
        if (overlap == 2)
            SecondLevelOverlapDec(buf, p0, p1, left, right, top, bottom);

        // ---- first level inverse transform (strIDCT4x4Stage1, staggered across the MB window) ----
        if (!top)
        {
            for (var j = left ? 32 : -96; j < (right ? 32 : 160); j += 64)
            {
                PhotoCoreTransform.InverseStage1(buf.Slice(p0 + j + 0, 16));
                PhotoCoreTransform.InverseStage1(buf.Slice(p0 + j + 16, 16));
            }
        }
        if (!bottom)
        {
            for (var j = left ? 0 : -128; j < (right ? 0 : 128); j += 64)
            {
                PhotoCoreTransform.InverseStage1(buf.Slice(p1 + j + 0, 16));
                PhotoCoreTransform.InverseStage1(buf.Slice(p1 + j + 16, 16));
            }
        }

        // ---- first level inverse overlap (OL_ONE / OL_TWO) ----
        if (overlap != 0)
            FirstLevelOverlapDec(buf, p0, p1, left, right, top, bottom);
    }

    // ===================================================================== first-level overlap

    // strFwdTransform.c:556-648 — first-level (block-boundary) forward overlap, 444 branch,
    // single-tile (all tile-boundary predicates constant FALSE). Operates on the post-color,
    // pre-transform samples across the 2-MB-row window.
    private static void FirstLevelOverlapEnc(Span<int> b, int p0, int p1, bool left, bool right, bool top, bool bottom)
    {
        int p;

        // Corner operations
        if (top && left)
            Pre4(ref b[p1 + 0], ref b[p1 + 1], ref b[p1 + 2], ref b[p1 + 3]);
        if (top && right)
            Pre4(ref b[p1 - 59], ref b[p1 - 60], ref b[p1 - 57], ref b[p1 - 58]);
        if (bottom && left)
            Pre4(ref b[p0 + 48 + 10], ref b[p0 + 48 + 11], ref b[p0 + 48 + 8], ref b[p0 + 48 + 9]);
        if (bottom && right)
            Pre4(ref b[p0 - 1], ref b[p0 - 2], ref b[p0 - 3], ref b[p0 - 4]);

        if (!right && !bottom)
        {
            if (top)
            {
                for (var j = left ? 0 : -64; j < 192; j += 64)
                {
                    p = p1 + j;
                    Pre4(ref b[p + 5], ref b[p + 4], ref b[p + 64], ref b[p + 65]);
                    Pre4(ref b[p + 7], ref b[p + 6], ref b[p + 66], ref b[p + 67]);
                }
            }
            else
            {
                for (var j = left ? 0 : -64; j < 192; j += 64)
                    PreStage1Split(b, p0 + 48 + j, p1 + j, 0);
            }

            if (left)
            {
                if (!top)
                {
                    Pre4(ref b[p0 + 58], ref b[p0 + 56], ref b[p1 + 0], ref b[p1 + 2]);
                    Pre4(ref b[p0 + 59], ref b[p0 + 57], ref b[p1 + 1], ref b[p1 + 3]);
                }
                for (var j = -64; j < -16; j += 16)
                {
                    p = p1 + j;
                    Pre4(ref b[p + 74], ref b[p + 72], ref b[p + 80], ref b[p + 82]);
                    Pre4(ref b[p + 75], ref b[p + 73], ref b[p + 81], ref b[p + 83]);
                }
            }
            else
            {
                for (var j = -64; j < -16; j += 16)
                    PreStage1(b, p1 + j, 0);
            }

            PreStage1(b, p1 + 0, 0);
            PreStage1(b, p1 + 16, 0);
            PreStage1(b, p1 + 32, 0);
            PreStage1(b, p1 + 64, 0);
            PreStage1(b, p1 + 80, 0);
            PreStage1(b, p1 + 96, 0);
            PreStage1(b, p1 + 128, 0);
            PreStage1(b, p1 + 144, 0);
            PreStage1(b, p1 + 160, 0);
        }

        if (bottom)
        {
            for (var j = left ? 48 : -16; j < (right ? -16 : 240); j += 64)
            {
                p = p0 + j;
                Pre4(ref b[p + 15], ref b[p + 14], ref b[p + 74], ref b[p + 75]);
                Pre4(ref b[p + 13], ref b[p + 12], ref b[p + 72], ref b[p + 73]);
            }
        }

        if (right && !bottom)
        {
            if (!top)
            {
                Pre4(ref b[p0 - 1], ref b[p0 - 3], ref b[p1 - 59], ref b[p1 - 57]);
                Pre4(ref b[p0 - 2], ref b[p0 - 4], ref b[p1 - 60], ref b[p1 - 58]);
            }
            for (var j = -64; j < -16; j += 16)
            {
                p = p1 + j;
                Pre4(ref b[p + 15], ref b[p + 13], ref b[p + 21], ref b[p + 23]);
                Pre4(ref b[p + 14], ref b[p + 12], ref b[p + 20], ref b[p + 22]);
            }
        }
    }

    // strInvTransform.c:1351-1457 — first-level inverse overlap, _alternate operators,
    // 444 branch, single-tile. Exact inverse of FirstLevelOverlapEnc.
    private static void FirstLevelOverlapDec(Span<int> b, int p0, int p1, bool left, bool right, bool top, bool bottom)
    {
        bool topORbottom = top || bottom;
        int p;

        if (left || right)
        {
            // Corner operations
            if (top && left)
                Post4Alt(ref b[p1 + 0], ref b[p1 + 1], ref b[p1 + 2], ref b[p1 + 3]);
            if (top && right)
                Post4Alt(ref b[p1 - 59], ref b[p1 - 60], ref b[p1 - 57], ref b[p1 - 58]);
            if (bottom && left)
                Post4Alt(ref b[p0 + 48 + 10], ref b[p0 + 48 + 11], ref b[p0 + 48 + 8], ref b[p0 + 48 + 9]);
            if (bottom && right)
                Post4Alt(ref b[p0 - 1], ref b[p0 - 2], ref b[p0 - 3], ref b[p0 - 4]);

            if (left)
            {
                int j = 0 + 10;
                if (!top)
                {
                    p = p0 + 16 + j;
                    Post4Alt(ref b[p + 0], ref b[p - 2], ref b[p + 6], ref b[p + 8]);
                    Post4Alt(ref b[p + 1], ref b[p - 1], ref b[p + 7], ref b[p + 9]);
                    Post4Alt(ref b[p + 16], ref b[p + 14], ref b[p + 22], ref b[p + 24]);
                    Post4Alt(ref b[p + 17], ref b[p + 15], ref b[p + 23], ref b[p + 25]);
                }
                if (!bottom)
                {
                    p = p1 + j;
                    Post4Alt(ref b[p + 0], ref b[p - 2], ref b[p + 6], ref b[p + 8]);
                    Post4Alt(ref b[p + 1], ref b[p - 1], ref b[p + 7], ref b[p + 9]);
                }
                if (!topORbottom)
                {
                    Post4Alt(ref b[p0 + 48 + j + 0], ref b[p0 + 48 + j - 2], ref b[p1 - 10 + j], ref b[p1 - 8 + j]);
                    Post4Alt(ref b[p0 + 48 + j + 1], ref b[p0 + 48 + j - 1], ref b[p1 - 9 + j], ref b[p1 - 7 + j]);
                }
            }
            if (right)
            {
                int j = -64 + 14;
                if (!top)
                {
                    p = p0 + 16 + j;
                    Post4Alt(ref b[p + 0], ref b[p - 2], ref b[p + 6], ref b[p + 8]);
                    Post4Alt(ref b[p + 1], ref b[p - 1], ref b[p + 7], ref b[p + 9]);
                    Post4Alt(ref b[p + 16], ref b[p + 14], ref b[p + 22], ref b[p + 24]);
                    Post4Alt(ref b[p + 17], ref b[p + 15], ref b[p + 23], ref b[p + 25]);
                }
                if (!bottom)
                {
                    p = p1 + j;
                    Post4Alt(ref b[p + 0], ref b[p - 2], ref b[p + 6], ref b[p + 8]);
                    Post4Alt(ref b[p + 1], ref b[p - 1], ref b[p + 7], ref b[p + 9]);
                }
                if (!topORbottom)
                {
                    Post4Alt(ref b[p0 + 48 + j + 0], ref b[p0 + 48 + j - 2], ref b[p1 - 10 + j], ref b[p1 - 8 + j]);
                    Post4Alt(ref b[p0 + 48 + j + 1], ref b[p0 + 48 + j - 1], ref b[p1 - 9 + j], ref b[p1 - 7 + j]);
                }
            }
        }

        if (top)
        {
            for (var j = left ? 0 : -192; j < (right ? -64 : 64); j += 64)
            {
                p = p1 + j;
                Post4Alt(ref b[p + 5], ref b[p + 4], ref b[p + 64], ref b[p + 65]);
                Post4Alt(ref b[p + 7], ref b[p + 6], ref b[p + 66], ref b[p + 67]);
                PostStage1Alt(b, p1 + j, 0);
            }
        }

        if (bottom)
        {
            for (var j = left ? 0 : -192; j < (right ? -64 : 64); j += 64)
            {
                PostStage1Alt(b, p0 + 16 + j, 0);
                PostStage1Alt(b, p0 + 32 + j, 0);
                p = p0 + 48 + j;
                Post4Alt(ref b[p + 15], ref b[p + 14], ref b[p + 74], ref b[p + 75]);
                Post4Alt(ref b[p + 13], ref b[p + 12], ref b[p + 72], ref b[p + 73]);
            }
        }

        if (!top && !bottom)
        {
            for (var j = left ? 0 : -192; j < (right ? -64 : 64); j += 64)
            {
                PostStage1Alt(b, p0 + 16 + j, 0);
                PostStage1Alt(b, p0 + 32 + j, 0);
                PostStage1SplitAlt(b, p0 + 48 + j, p1 + j, 0);
                PostStage1Alt(b, p1 + j, 0);
            }
        }
    }

    // ===================================================================== second-level overlap

    // strFwdTransform.c:673-719 — second-level (MB-boundary) forward overlap, 444 branch,
    // single-tile. Operates on the super-DC coefficients after the first-level transform.
    private static void SecondLevelOverlapEnc(Span<int> b, int p0, int p1, bool left, bool right, bool top, bool bottom)
    {
        bool topORbottom = top || bottom, leftORright = left || right;
        int p;

        // Corner operations
        if (top && left)
            Pre4(ref b[p1 + 0], ref b[p1 + 64], ref b[p1 + 0 + 16], ref b[p1 + 64 + 16]);
        if (top && right)
            Pre4(ref b[p1 - 128], ref b[p1 - 64], ref b[p1 - 128 + 16], ref b[p1 - 64 + 16]);
        if (bottom && left)
            Pre4(ref b[p0 + 32], ref b[p0 + 96], ref b[p0 + 32 + 16], ref b[p0 + 96 + 16]);
        if (bottom && right)
            Pre4(ref b[p0 - 96], ref b[p0 - 32], ref b[p0 - 96 + 16], ref b[p0 - 32 + 16]);

        if (leftORright && !topORbottom)
        {
            if (left)
            {
                int j = 0;
                Pre4(ref b[p0 + j + 32], ref b[p0 + j + 48], ref b[p1 + j + 0], ref b[p1 + j + 16]);
                Pre4(ref b[p0 + j + 96], ref b[p0 + j + 112], ref b[p1 + j + 64], ref b[p1 + j + 80]);
            }
            if (right)
            {
                int j = -128;
                Pre4(ref b[p0 + j + 32], ref b[p0 + j + 48], ref b[p1 + j + 0], ref b[p1 + j + 16]);
                Pre4(ref b[p0 + j + 96], ref b[p0 + j + 112], ref b[p1 + j + 64], ref b[p1 + j + 80]);
            }
        }

        if (!leftORright)
        {
            if (topORbottom)
            {
                if (top)
                {
                    p = p1;
                    Pre4(ref b[p - 128], ref b[p - 64], ref b[p + 0], ref b[p + 64]);
                    Pre4(ref b[p - 112], ref b[p - 48], ref b[p + 16], ref b[p + 80]);
                }
                if (bottom)
                {
                    p = p0 + 32;
                    Pre4(ref b[p - 128], ref b[p - 64], ref b[p + 0], ref b[p + 64]);
                    Pre4(ref b[p - 112], ref b[p - 48], ref b[p + 16], ref b[p + 80]);
                }
            }
            else
            {
                PreStage2Split(b, p0, p1);
            }
        }
    }

    // strInvTransform.c:1271-1312 — second-level inverse overlap, _alternate operators,
    // 444 branch, single-tile. Exact inverse of SecondLevelOverlapEnc.
    private static void SecondLevelOverlapDec(Span<int> b, int p0, int p1, bool left, bool right, bool top, bool bottom)
    {
        bool topORbottom = top || bottom, leftORright = left || right;
        int p;

        // Corner operations
        if (top && left)
            Post4Alt(ref b[p1 + 0], ref b[p1 + 64], ref b[p1 + 0 + 16], ref b[p1 + 64 + 16]);
        if (top && right)
            Post4Alt(ref b[p1 - 128], ref b[p1 - 64], ref b[p1 - 128 + 16], ref b[p1 - 64 + 16]);
        if (bottom && left)
            Post4Alt(ref b[p0 + 32], ref b[p0 + 96], ref b[p0 + 32 + 16], ref b[p0 + 96 + 16]);
        if (bottom && right)
            Post4Alt(ref b[p0 - 96], ref b[p0 - 32], ref b[p0 - 96 + 16], ref b[p0 - 32 + 16]);

        if (leftORright && !topORbottom)
        {
            if (left)
            {
                int j = 0;
                Post4Alt(ref b[p0 + j + 32], ref b[p0 + j + 48], ref b[p1 + j + 0], ref b[p1 + j + 16]);
                Post4Alt(ref b[p0 + j + 96], ref b[p0 + j + 112], ref b[p1 + j + 64], ref b[p1 + j + 80]);
            }
            if (right)
            {
                int j = -128;
                Post4Alt(ref b[p0 + j + 32], ref b[p0 + j + 48], ref b[p1 + j + 0], ref b[p1 + j + 16]);
                Post4Alt(ref b[p0 + j + 96], ref b[p0 + j + 112], ref b[p1 + j + 64], ref b[p1 + j + 80]);
            }
        }

        if (!leftORright)
        {
            if (topORbottom)
            {
                if (top)
                {
                    p = p1;
                    Post4Alt(ref b[p - 128], ref b[p - 64], ref b[p + 0], ref b[p + 64]);
                    Post4Alt(ref b[p - 112], ref b[p - 48], ref b[p + 16], ref b[p + 80]);
                }
                if (bottom)
                {
                    p = p0 + 32;
                    Post4Alt(ref b[p - 128], ref b[p - 64], ref b[p + 0], ref b[p + 64]);
                    Post4Alt(ref b[p - 112], ref b[p - 48], ref b[p + 16], ref b[p + 80]);
                }
            }
            if (!topORbottom)
                PostStage2SplitAlt(b, p0, p1);
        }
    }
}
