namespace SharpAstro.Jxr;

/// <summary>
/// Chroma downsampling on encode — the C# port of jxrlib's <c>downsampleUV</c>
/// (strenc.c). For YUV420 / YUV422 the full-resolution chroma (256 ints per
/// macroblock, <c>idxCC</c> layout, already colour-transformed to YCoCg-R and
/// <c>&lt;&lt;3</c>-scaled) is reduced to 64 (420) / 128 (422) ints per MB before
/// the transform — the inverse of <see cref="ChromaUpsample"/>.
///
/// <para>The filter is the separable 5-tap <c>[1,4,6,4,1]/16</c>
/// (jxrlib <c>DF_ODD</c>): 4:4:4 → 4:2:2 is one horizontal pass; 4:4:4 → 4:2:0 is
/// the same horizontal pass followed by a vertical pass. Each output sample is
/// centred on an even full-resolution sample; the window <b>reflects</b> at the
/// top/left boundary (sample −1 ↦ +1, −2 ↦ +2) and at the bottom/right boundary
/// the +2 tap collapses onto the centre (jxrlib's <c>d4 = d2</c>), while the +1 tap
/// is the last (edge-replicated) padded sample. We run it as a clean whole-image
/// separable filter — equivalent to jxrlib's streaming per-MB-row form, whose
/// cross-MB-row buffer juggling exists only to keep the vertical filter continuous
/// over the global row grid.</para>
/// </summary>
internal static class ChromaDownsample
{
    // strenc.c:1603 DF_ODD — 5-tap [1,4,6,4,1]/16 with rounding: (d0 + 4·d1 + 6·d2 + 4·d3 + d4 + 8) >> 4.
    private static int Filter(int d0, int d1, int d2, int d3, int d4)
        => (((d1 + d2 + d3) << 2) + (d2 << 1) + d0 + d4 + 8) >> 4;

    // Downsample one line of <paramref name="n"/> samples (stride <paramref name="srcStride"/> from
    // <paramref name="srcBase"/>) to n/2 samples (stride <paramref name="dstStride"/> from
    // <paramref name="dstBase"/>), each centred on an even source sample with reflect-at-start /
    // collapse-+2-tap-at-end boundaries.
    private static void DownsampleLine(int[] src, int srcBase, int srcStride, int n, int[] dst, int dstBase, int dstStride)
    {
        int half = n / 2;
        for (var k = 0; k < half; k++)
        {
            int c = 2 * k;
            int d2 = src[srcBase + c * srcStride];
            int d1 = src[srcBase + (c >= 1 ? c - 1 : 1) * srcStride];     // reflect: -1 -> +1
            int d0 = src[srcBase + (c >= 2 ? c - 2 : 2) * srcStride];     // reflect: -2 -> +2
            int d3 = src[srcBase + (c + 1) * srcStride];                  // c+1 <= n-1 always (c <= n-2)
            int d4 = src[srcBase + (c + 2 <= n - 1 ? c + 2 : c) * srcStride]; // +2 collapses onto centre at the end
            dst[dstBase + k * dstStride] = Filter(d0, d1, d2, d3, d4);
        }
    }

    /// <summary>
    /// Downsample full-resolution chroma <paramref name="fullU"/>/<paramref name="fullV"/>
    /// (256 ints per MB, <c>idxCC</c> layout, in the <see cref="OverlapTransform"/> slack grid)
    /// into reduced chroma <paramref name="redU"/>/<paramref name="redV"/> (the
    /// <see cref="ChromaOverlapTransform"/> slack grid, 64/128 ints per MB in the
    /// <c>idxCC_420</c>/<c>idxCC</c> reduced layout) over an <paramref name="mbCols"/> ×
    /// <paramref name="mbRows"/> macroblock grid.
    /// </summary>
    public static void Downsample(ColorFormat cf, int[] fullU, int[] fullV, int[] redU, int[] redV, int mbCols, int mbRows)
    {
        int height = mbRows * 16, width = mbCols * 16, wr = width / 2;
        var idx = SignalTransform.IdxCc;

        // Gather full-res chroma out of the idxCC plane into plain row-major [height][width].
        var fU = new int[height * width];
        var fV = new int[height * width];
        for (var gr = 0; gr < height; gr++)
        {
            int mbR = gr >> 4, rr = gr & 15;
            for (var gc = 0; gc < width; gc++)
            {
                int off = OverlapTransform.MbBase(mbCols, mbR, gc >> 4) + idx[rr * 16 + (gc & 15)];
                fU[gr * width + gc] = fullU[off];
                fV[gr * width + gc] = fullV[off];
            }
        }

        // Horizontal pass (444 -> 422), both formats: width -> wr.
        var hU = new int[height * wr];
        var hV = new int[height * wr];
        for (var gr = 0; gr < height; gr++)
        {
            DownsampleLine(fU, gr * width, 1, width, hU, gr * wr, 1);
            DownsampleLine(fV, gr * width, 1, width, hV, gr * wr, 1);
        }

        if (cf == ColorFormat.Yuv422)
        {
            // Scatter into the reduced 422 buffer (128/MB, idxCC[row][0..7]).
            for (var gr = 0; gr < height; gr++)
            {
                int mbR = gr >> 4, rr = gr & 15;
                for (var k = 0; k < wr; k++)
                {
                    int off = ChromaOverlapTransform.MbBase(mbCols, mbR, k >> 3, cf) + idx[rr * 16 + (k & 7)];
                    redU[off] = hU[gr * wr + k];
                    redV[off] = hV[gr * wr + k];
                }
            }
            return;
        }

        // 420: vertical pass (422 -> 420) on the horizontally-downsampled planes: height -> hr.
        int hr = height / 2;
        var vU = new int[hr * wr];
        var vV = new int[hr * wr];
        for (var k = 0; k < wr; k++)
        {
            DownsampleLine(hU, k, wr, height, vU, k, wr);
            DownsampleLine(hV, k, wr, height, vV, k, wr);
        }

        var idx420 = ChromaUpsample.IdxCc420;
        for (var r = 0; r < hr; r++)
        {
            int mbR = r >> 3, rrr = r & 7;
            for (var k = 0; k < wr; k++)
            {
                int off = ChromaOverlapTransform.MbBase(mbCols, mbR, k >> 3, cf) + idx420[rrr * 8 + (k & 7)];
                redU[off] = vU[r * wr + k];
                redV[off] = vV[r * wr + k];
            }
        }
    }
}
