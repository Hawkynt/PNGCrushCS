namespace SharpAstro.Jxr;

/// <summary>
/// Chroma upsampling on decode — the C# port of jxrlib's <c>interpolateUV</c>
/// (strdec.c). Reconstructed chroma for YUV420 / YUV422 lives at reduced
/// resolution (64 / 128 ints per macroblock, in the <c>idxCC_420</c> / <c>idxCC</c>
/// block-scrambled layout); to invert the YCoCg-R colour transform at full
/// resolution we bilinearly interpolate it up to the luma grid (256 ints per MB).
///
/// <para>422 → 444 is a horizontal interpolation; 420 → 444 is a vertical pass
/// (which peeks the next macroblock row for the bottom edge) followed by a
/// horizontal pass. jxrlib streams one MB row at a time using <c>a0MBbuffer</c>
/// (current) and <c>a1MBbuffer</c> (next); our codec reconstructs whole-image
/// planes, so the "next MB row" is simply the next row of the same source plane.
/// Source/destination indices add a per-MB-row base on top of jxrlib's in-row
/// index. Replicate at the right/bottom image edges, exactly as jxrlib does.</para>
/// </summary>
internal static class ChromaUpsample
{
    // strcodec.c idxCC_420[8][8] flattened to [row*8 + col] — reduced (8x8) chroma
    // pixel position -> plane position within a 64-int 420 chroma macroblock.
    // Internal so component tests can populate the reduced chroma plane.
    internal static readonly int[] IdxCc420 =
    {
        0x00, 0x01, 0x05, 0x04, 0x20, 0x21, 0x25, 0x24,
        0x02, 0x03, 0x07, 0x06, 0x22, 0x23, 0x27, 0x26,
        0x0a, 0x0b, 0x0f, 0x0e, 0x2a, 0x2b, 0x2f, 0x2e,
        0x08, 0x09, 0x0d, 0x0c, 0x28, 0x29, 0x2d, 0x2c,
        0x10, 0x11, 0x15, 0x14, 0x30, 0x31, 0x35, 0x34,
        0x12, 0x13, 0x17, 0x16, 0x32, 0x33, 0x37, 0x36,
        0x1a, 0x1b, 0x1f, 0x1e, 0x3a, 0x3b, 0x3f, 0x3e,
        0x18, 0x19, 0x1d, 0x1c, 0x38, 0x39, 0x3d, 0x3c,
    };

    /// <summary>Ints per macroblock in the reduced chroma plane (jxrlib <c>cblkChromas[cf]*16</c>).</summary>
    public static int ReducedStride(ColorFormat cf) => cf switch
    {
        ColorFormat.Yuv420 => 64,
        ColorFormat.Yuv422 => 128,
        _ => 256,
    };

    /// <summary>
    /// Upsample reduced chroma (<paramref name="srcU"/>/<paramref name="srcV"/>, stride
    /// <see cref="ReducedStride"/> per MB) into full-resolution chroma planes
    /// (<paramref name="dstU"/>/<paramref name="dstV"/>, 256 ints per MB, <c>idxCC</c> layout)
    /// over an <paramref name="mbCols"/> × <paramref name="mbRows"/> macroblock grid.
    /// </summary>
    /// <param name="dstRowStride">Ints per destination MB-row. 0 ⇒ the simple/packed layout
    /// (<c>mbCols*256</c>); pass <c>(mbCols+1)*256</c> for the <see cref="OverlapTransform"/>
    /// slack layout so the upsampled chroma aligns with the luma plane for <c>StoreColor</c>.</param>
    /// <param name="srcRowStride">Ints per source (reduced-chroma) MB-row. 0 ⇒ the packed layout
    /// (<c>mbCols*ReducedStride</c>); pass <c>(mbCols+1)*ReducedStride</c> for the
    /// <see cref="ChromaOverlapTransform"/> slack layout. The within-row MB stride is unchanged
    /// (the slack is one extra MB-slot per row), so only the per-row base differs.</param>
    public static void Interpolate(ColorFormat cf, int[] srcU, int[] srcV, int[] dstU, int[] dstV,
                                   int mbCols, int mbRows, int dstRowStride = 0, int srcRowStride = 0)
    {
        int ds = dstRowStride == 0 ? mbCols * 256 : dstRowStride;
        int ss = srcRowStride == 0 ? mbCols * ReducedStride(cf) : srcRowStride;
        switch (cf)
        {
            case ColorFormat.Yuv422: Interpolate422(srcU, srcV, dstU, dstV, mbCols, mbRows, ds, ss); break;
            case ColorFormat.Yuv420: Interpolate420(srcU, srcV, dstU, dstV, mbCols, mbRows, ds, ss); break;
            default: throw new NotSupportedException($"Chroma upsampling only applies to YUV420/422 (got {cf}).");
        }
    }

    // 422 => 444: interpolate horizontally. strdec.c:522-547.
    private static void Interpolate422(int[] srcU, int[] srcV, int[] dstU, int[] dstV, int mbCols, int mbRows, int dstStride, int srcStride)
    {
        var idx = SignalTransform.IdxCc;
        int cWidth = mbCols * 16; // full luma-resolution pixel width of one MB row
        for (var mbRow = 0; mbRow < mbRows; mbRow++)
        {
            int srcBase = mbRow * srcStride;
            int dstBase = mbRow * dstStride;
            for (var iRow = 0; iRow < 16; iRow++)
            {
                int iIdxD = 0;
                for (var iColumn = 0; iColumn < cWidth; iColumn += 2)
                {
                    int iIdxS = srcBase + ((iColumn >> 4) << 7) + idx[iRow * 16 + ((iColumn >> 1) & 7)];
                    iIdxD = dstBase + ((iColumn >> 4) << 8) + idx[iRow * 16 + (iColumn & 15)];
                    dstU[iIdxD] = srcU[iIdxS];
                    dstV[iIdxD] = srcV[iIdxS];

                    if (iColumn > 0)
                    {
                        int iL = iColumn - 2, iIdxL = dstBase + ((iL >> 4) << 8) + idx[iRow * 16 + (iL & 15)];
                        int iC = iColumn - 1, iIdxC = dstBase + ((iC >> 4) << 8) + idx[iRow * 16 + (iC & 15)];
                        dstU[iIdxC] = (dstU[iIdxL] + dstU[iIdxD] + 1) >> 1;
                        dstV[iIdxC] = (dstV[iIdxL] + dstV[iIdxD] + 1) >> 1;
                    }
                }

                // last pixel (rightmost odd column) replicates the last even column.
                int last = dstBase + (((cWidth - 1) >> 4) << 8) + idx[iRow * 16 + ((cWidth - 1) & 15)];
                dstU[last] = dstU[iIdxD];
                dstV[last] = dstV[iIdxD];
            }
        }
    }

    // 420 => 444: interpolate vertically (peeking the next MB row at the bottom edge),
    // then horizontally. strdec.c:548-603 with cShift = 4 (444 output).
    private static void Interpolate420(int[] srcU, int[] srcV, int[] dstU, int[] dstV, int mbCols, int mbRows, int dstStride, int srcStride)
    {
        var idx = SignalTransform.IdxCc;
        var idx420 = IdxCc420;
        int cWidth = mbCols * 16;
        for (var mbRow = 0; mbRow < mbRows; mbRow++)
        {
            int srcBase = mbRow * srcStride;
            int nextSrcBase = (mbRow + 1) * srcStride; // next MB row (a1MBbuffer)
            int dstBase = mbRow * dstStride;
            bool bottom = mbRow == mbRows - 1;

            // --- vertical pass: fill even rows from reduced source, interpolate odd rows ---
            for (var iColumn = 0; iColumn < cWidth; iColumn += 2)
            {
                int cMB = dstBase + ((iColumn >> 4) << 8);
                int cPix = iColumn & 15;
                int iIdxD = 0;
                for (var iRow = 0; iRow < 16; iRow += 2)
                {
                    int iIdxS = srcBase + ((iColumn >> 4) << 6) + idx420[(iRow >> 1) * 8 + ((iColumn >> 1) & 7)];
                    iIdxD = cMB + idx[iRow * 16 + cPix];
                    dstU[iIdxD] = srcU[iIdxS];
                    dstV[iIdxD] = srcV[iIdxS];

                    if (iRow > 0)
                    {
                        int iIdxT = cMB + idx[(iRow - 2) * 16 + cPix];
                        int iIdxC = cMB + idx[(iRow - 1) * 16 + cPix];
                        dstU[iIdxC] = (dstU[iIdxT] + dstU[iIdxD] + 1) >> 1;
                        dstV[iIdxC] = (dstV[iIdxT] + dstV[iIdxD] + 1) >> 1;
                    }
                }

                // last row (row 15): replicate at the image bottom, else interpolate with the next MB row.
                int iIdxLast = cMB + idx[15 * 16 + cPix];
                if (bottom)
                {
                    dstU[iIdxLast] = dstU[iIdxD];
                    dstV[iIdxLast] = dstV[iIdxD];
                }
                else
                {
                    int iIdxB = nextSrcBase + ((iColumn >> 4) << 6) + idx420[0 * 8 + ((iColumn >> 1) & 7)];
                    dstU[iIdxLast] = (srcU[iIdxB] + dstU[iIdxD] + 1) >> 1;
                    dstV[iIdxLast] = (srcV[iIdxB] + dstV[iIdxD] + 1) >> 1;
                }
            }

            // --- horizontal pass: fill the odd columns from their even-column neighbours ---
            for (var iRow = 0; iRow < 16; iRow++)
            {
                int iIdxS = 0;
                for (var iColumn = 1; iColumn < cWidth - 2; iColumn += 2)
                {
                    int iIdxL = dstBase + (((iColumn - 1) >> 4) << 8) + idx[iRow * 16 + ((iColumn - 1) & 15)];
                    int iIdxD = dstBase + ((iColumn >> 4) << 8) + idx[iRow * 16 + (iColumn & 15)];
                    iIdxS = dstBase + (((iColumn + 1) >> 4) << 8) + idx[iRow * 16 + ((iColumn + 1) & 15)];
                    dstU[iIdxD] = (dstU[iIdxS] + dstU[iIdxL] + 1) >> 1;
                    dstV[iIdxD] = (dstV[iIdxS] + dstV[iIdxL] + 1) >> 1;
                }

                // last pixel (rightmost column) replicates the last interpolated even column.
                int iIdxDlast = dstBase + (((cWidth - 1) >> 4) << 8) + idx[iRow * 16 + ((cWidth - 1) & 15)];
                dstU[iIdxDlast] = dstU[iIdxS];
                dstV[iIdxDlast] = dstV[iIdxS];
            }
        }
    }
}
