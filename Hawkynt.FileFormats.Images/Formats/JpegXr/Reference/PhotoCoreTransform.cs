namespace SharpAstro.Jxr;

/// <summary>
/// The JPEG XR Photo Core Transform (PCT) — the reversible integer
/// lapped-transform at the heart of T.832. A 16×16 macroblock is transformed in
/// two stages: stage 1 runs a 4×4 PCT on each of the sixteen 4×4 blocks; stage 2
/// runs the same 4×4 PCT on the 4×4 grid of stage-1 DC coefficients (the
/// "super-DC" block). The forward/inverse pair round-trips bit-exactly.
/// </summary>
/// <remarks>
/// Ported faithfully from Microsoft's jxrlib reference codec:
/// <list type="bullet">
/// <item><c>image/sys/strTransform.c</c> — the 2×2 cores (<c>strDCT2x2dn/up</c>).</item>
/// <item><c>image/encode/strFwdTransform.c</c> — <c>strDCT4x4Stage1</c>,
/// <c>strDCT4x4SecondStage</c>, <c>fwdOdd</c>, <c>fwdOddOdd</c>.</item>
/// <item><c>image/decode/strInvTransform.c</c> — <c>strIDCT4x4Stage1/Stage2</c>,
/// <c>invOdd</c>, <c>invOddOdd</c>.</item>
/// </list>
/// Every shift, rounding bias, and sign convention is preserved exactly; these
/// are the reversible-integer details a spec read tends to get subtly wrong.
///
/// <para>The data order within a 4×4 block after the forward transform is the
/// reference's permuted layout (DC at index 0); the dctIndex / zigzag tables
/// downstream consume that layout — do not "fix" the ordering here.</para>
/// </remarks>
internal static class PhotoCoreTransform
{
    // strFwdTransform.c:34  ROTATE2(a, b): b -= (a*3 + 4) >> 3, a += (b*3 + 4) >> 3
    // (comma operator — b is updated first using the old a, then a uses the new b)
    private static void Rotate2(ref int a, ref int b)
    {
        b -= (a * 3 + 4) >> 3;
        a += (b * 3 + 4) >> 3;
    }

    // strInvTransform.c:35  IROTATE2(a, b): a -= (b*3 + 4) >> 3, b += (a*3 + 4) >> 3
    private static void IRotate2(ref int a, ref int b)
    {
        a -= (b * 3 + 4) >> 3;
        b += (a * 3 + 4) >> 3;
    }

    // strTransform.c  strDCT2x2dn — the 2×2 reversible core (self-inverse).
    private static void Dct2x2dn(Span<int> p, int ia, int ib, int ic, int id)
    {
        int a = p[ia], b = p[ib], inC = p[ic], d = p[id];
        a += d;
        b -= inC;
        int t = (a - b) >> 1;
        int c = t - d;
        d = t - inC;
        a -= d;
        b += c;
        p[ia] = a; p[ib] = b; p[ic] = c; p[id] = d;
    }

    // strInvTransform.c strDCT2x2dnDec — the 2×2 reversible core followed by an ×2 post-scale on
    // all four outputs. jxrlib uses this (not the plain dn) for the second-stage chroma inverse in
    // scaled-arithmetic mode, which YUV420/422 always run in.
    private static void Dct2x2dnDec(Span<int> p, int ia, int ib, int ic, int id)
    {
        int a = p[ia], b = p[ib], inC = p[ic], d = p[id];
        a += d;
        b -= inC;
        int t = (a - b) >> 1;
        int c = t - d;
        d = t - inC;
        a -= d;
        b += c;
        p[ia] = a * 2; p[ib] = b * 2; p[ic] = c * 2; p[id] = d * 2;
    }

    // strFwdTransform.c strDCT2x2dnEnc — an ×½ pre-scale (>>1) on all four inputs followed by the
    // 2×2 core. The forward (encode) counterpart of <see cref="Dct2x2dnDec"/>; the >>1/×2 pair
    // round-trips losslessly for the even-valued chroma DCs produced by the scaled-mode pipeline.
    private static void Dct2x2dnEnc(Span<int> p, int ia, int ib, int ic, int id)
    {
        int a = p[ia] >> 1, b = p[ib] >> 1, inC = p[ic] >> 1, d = p[id] >> 1;
        a += d;
        b -= inC;
        int t = (a - b) >> 1;
        int c = t - d;
        d = t - inC;
        a -= d;
        b += c;
        p[ia] = a; p[ib] = b; p[ic] = c; p[id] = d;
    }

    // strTransform.c  strDCT2x2up — same as dn but with +1 rounding on the half-sum.
    private static void Dct2x2up(Span<int> p, int ia, int ib, int ic, int id)
    {
        int a = p[ia], b = p[ib], inC = p[ic], d = p[id];
        a += d;
        b -= inC;
        int t = (a - b + 1) >> 1;
        int c = t - d;
        d = t - inC;
        a -= d;
        b += c;
        p[ia] = a; p[ib] = b; p[ic] = c; p[id] = d;
    }

    // strTransform.c  FOURBUTTERFLY(p, ...) — four 2×2 (dn) butterflies on the given index quads.
    private static void FourButterflyDn(
        Span<int> p,
        int i00, int i01, int i02, int i03,
        int i10, int i11, int i12, int i13,
        int i20, int i21, int i22, int i23,
        int i30, int i31, int i32, int i33)
    {
        Dct2x2dn(p, i00, i01, i02, i03);
        Dct2x2dn(p, i10, i11, i12, i13);
        Dct2x2dn(p, i20, i21, i22, i23);
        Dct2x2dn(p, i30, i31, i32, i33);
    }

    // strTransform.c  FOURBUTTERFLY_HARDCODED1 — the contiguous-block (stride-1) butterfly.
    private static void FourButterflyHardcoded1(Span<int> p)
        => FourButterflyDn(p, 0, 4, 8, 12, 1, 5, 9, 13, 2, 6, 10, 14, 3, 7, 11, 15);

    // strFwdTransform.c:386  fwdOddOdd — Kron(Rotate(pi/8), Rotate(pi/8)).
    // Note: loads b and c negated.
    private static void FwdOddOdd(Span<int> p, int ia, int ib, int ic, int id)
    {
        int a = p[ia], b = -p[ib], c = -p[ic], d = p[id];

        d += a;
        c -= b;
        int t1 = d >> 1; a -= t1;
        int t2 = c >> 1; b += t2;

        a += (b * 3 + 4) >> 3;
        b -= (a * 3 + 3) >> 2;
        a += (b * 3 + 3) >> 3;

        b -= t2;
        a += t1;
        c += b;
        d -= a;

        p[ia] = a; p[ib] = b; p[ic] = c; p[id] = d;
    }

    // strFwdTransform.c:451  fwdOdd — Kron(Rotate(pi/8), [1 1; 1 -1]/sqrt(2)).
    private static void FwdOdd(Span<int> p, int ia, int ib, int ic, int id)
    {
        int a = p[ia], b = p[ib], c = p[ic], d = p[id];

        b -= c;
        a += d;
        c += (b + 1) >> 1;
        d = ((a + 1) >> 1) - d;

        Rotate2(ref a, ref b);
        Rotate2(ref c, ref d);

        d += b >> 1;
        c -= (a + 1) >> 1;
        b -= d;
        a += c;

        p[ia] = a; p[ib] = b; p[ic] = c; p[id] = d;
    }

    // strInvTransform.c:589  invOddOdd — inverse of fwdOddOdd; writes b and c negated.
    private static void InvOddOdd(Span<int> p, int ia, int ib, int ic, int id)
    {
        int a = p[ia], b = p[ib], c = p[ic], d = p[id];

        d += a;
        c -= b;
        int t1 = d >> 1; a -= t1;
        int t2 = c >> 1; b += t2;

        a -= (b * 3 + 3) >> 3;
        b += (a * 3 + 3) >> 2;
        a -= (b * 3 + 4) >> 3;

        b -= t2;
        a += t1;
        c += b;
        d -= a;

        p[ia] = a; p[ib] = -b; p[ic] = -c; p[id] = d;
    }

    // strInvTransform.c:656  invOdd — inverse of fwdOdd.
    private static void InvOdd(Span<int> p, int ia, int ib, int ic, int id)
    {
        int a = p[ia], b = p[ib], c = p[ic], d = p[id];

        b += d;
        a -= c;
        d -= b >> 1;
        c += (a + 1) >> 1;

        IRotate2(ref a, ref b);
        IRotate2(ref c, ref d);

        c -= (b + 1) >> 1;
        d = ((a + 1) >> 1) - d;
        b += c;
        a -= d;

        p[ia] = a; p[ib] = b; p[ic] = c; p[id] = d;
    }

    // ---------------------------------------------------------------- stages

    /// <summary>
    /// Forward 4×4 PCT on a single 4×4 block (16 contiguous coefficients).
    /// jxrlib <c>strDCT4x4Stage1</c>.
    /// </summary>
    public static void ForwardStage1(Span<int> p)
    {
        FourButterflyHardcoded1(p);
        Dct2x2up(p, 0, 1, 2, 3);          // top-left corner
        FwdOddOdd(p, 15, 14, 13, 12);     // bottom-right corner
        FwdOdd(p, 5, 4, 7, 6);            // top-right corner
        FwdOdd(p, 10, 8, 11, 9);          // bottom-left corner
    }

    /// <summary>
    /// Forward 4×4 PCT on the 4×4 grid of stage-1 DC coefficients within a 16×16
    /// macroblock buffer (256 coefficients, the 16 touched positions at stride 16).
    /// jxrlib <c>strDCT4x4SecondStage</c>.
    /// </summary>
    public static void ForwardStage2(Span<int> p)
    {
        FourButterflyDn(p, 0, 192, 48, 240, 64, 128, 112, 176, 16, 208, 32, 224, 80, 144, 96, 160);
        Dct2x2up(p, 0, 64, 16, 80);
        FwdOddOdd(p, 160, 224, 176, 240);
        FwdOdd(p, 128, 192, 144, 208);
        FwdOdd(p, 32, 48, 96, 112);
    }

    /// <summary>Inverse of <see cref="ForwardStage1"/>. jxrlib <c>strIDCT4x4Stage1</c>.</summary>
    public static void InverseStage1(Span<int> p)
    {
        Dct2x2up(p, 0, 1, 2, 3);
        InvOdd(p, 5, 4, 7, 6);
        InvOdd(p, 10, 8, 11, 9);
        InvOddOdd(p, 15, 14, 13, 12);
        FourButterflyHardcoded1(p);
    }

    /// <summary>Inverse of <see cref="ForwardStage2"/>. jxrlib <c>strIDCT4x4Stage2</c>.</summary>
    public static void InverseStage2(Span<int> p)
    {
        InvOdd(p, 32, 48, 96, 112);
        InvOdd(p, 128, 192, 144, 208);
        InvOddOdd(p, 160, 224, 176, 240);
        Dct2x2up(p, 0, 64, 16, 80);
        FourButterflyDn(p, 0, 192, 48, 240, 64, 128, 112, 176, 16, 208, 32, 224, 80, 144, 96, 160);
    }

    /// <summary>
    /// Encoder chroma normalization after the second-stage transform: a right-shift
    /// by 1 on the DC positions (stride 16), only for chroma when scaled-arithmetic
    /// is active. jxrlib <c>strNormalizeEnc</c> (luma branch is a no-op).
    /// </summary>
    public static void NormalizeEnc(Span<int> p, bool chroma)
    {
        if (!chroma) return;
        for (var i = 0; i < 256; i += 16) p[i] >>= 1;
    }

    /// <summary>Decoder counterpart of <see cref="NormalizeEnc"/> (doubles the DC positions). jxrlib <c>strNormalizeDec</c>.</summary>
    public static void NormalizeDec(Span<int> p, bool chroma)
    {
        if (!chroma) return;
        for (var i = 0; i < 256; i += 16) p[i] += p[i];
    }

    // ---------------------------------------------------------------- chroma second stage
    // For the subsampled formats the chroma plane has only 4 (420) / 8 (422) blocks, so the
    // second stage is a 2×2 core on the block DCs (at blkOffsetUV positions) rather than the
    // full 4×4 strDCT4x4SecondStage. jxrlib strInvTransform.c (420_UV / 422_UV loops).

    /// <summary>
    /// YUV420 chroma second stage, <b>inverse</b> (decode): a single 2×2 core on the four block DCs
    /// (offsets {0,32,16,48} = <see cref="MacroblockLayout.BlkOffsetUV420"/>) with the ×2 post-scale
    /// (jxrlib <c>strDCT2x2dnDec</c>) — the always-scaled YUV420 path.
    /// </summary>
    public static void ChromaInverseStage2_420(Span<int> mb) => Dct2x2dnDec(mb, 0, 32, 16, 48);

    /// <summary>YUV420 chroma second stage, <b>forward</b> (encode): the ×½-prescale core
    /// (jxrlib <c>strDCT2x2dnEnc</c>), the inverse of <see cref="ChromaInverseStage2_420"/>.</summary>
    public static void ChromaForwardStage2_420(Span<int> mb) => Dct2x2dnEnc(mb, 0, 32, 16, 48);

    /// <summary>
    /// YUV422 chroma second stage, <b>inverse</b> (decode): a 1-D lossless Hadamard step on the
    /// two vertically-adjacent DCs (0,32) then two ×2-post-scaled 2×2 cores on {0,64,16,80} and
    /// {32,96,48,112} (jxrlib <c>strDCT2x2dnDec</c>) — the always-scaled YUV422 path.
    /// </summary>
    public static void ChromaInverseStage2_422(Span<int> mb)
    {
        mb[0] -= (mb[32] + 1) >> 1;
        mb[32] += mb[0];
        Dct2x2dnDec(mb, 0, 64, 16, 80);
        Dct2x2dnDec(mb, 32, 96, 48, 112);
    }

    /// <summary>
    /// YUV422 chroma second stage, <b>forward</b> (encode): the exact inverse of
    /// <see cref="ChromaInverseStage2_422"/> — the two ×½-prescaled 2×2 cores followed by the
    /// inverted Hadamard step.
    /// </summary>
    public static void ChromaForwardStage2_422(Span<int> mb)
    {
        Dct2x2dnEnc(mb, 0, 64, 16, 80);
        Dct2x2dnEnc(mb, 32, 96, 48, 112);
        mb[32] -= mb[0];
        mb[0] += (mb[32] + 1) >> 1;
    }
}
