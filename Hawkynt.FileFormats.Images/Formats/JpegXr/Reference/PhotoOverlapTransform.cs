namespace SharpAstro.Jxr;

/// <summary>
/// The JPEG XR Photo Overlap Transform (POT) — the lapped pre-/post-filter that
/// runs across block (and, at overlap level 2, macroblock) boundaries before the
/// forward <see cref="PhotoCoreTransform"/> on encode and after the inverse on
/// decode. It is what removes blocking artifacts at higher compression.
/// </summary>
/// <remarks>
/// Ported faithfully from jxrlib:
/// <list type="bullet">
/// <item><c>image/encode/strFwdTransform.c</c> — <c>strPre2/2x2/4</c>,
/// <c>strPre4x4Stage1/Stage2Split</c>, <c>strHSTenc/enc1/enc1_edge</c>,
/// <c>fwdOddOddPre</c>.</item>
/// <item><c>image/decode/strInvTransform.c</c> — <c>strPost2/2x2/4</c> (+ the
/// <c>_alternate</c> exact-inverse forms), <c>strPost4x4Stage1/Stage2Split</c>,
/// <c>strHSTdec/dec1/dec1_edge</c>, <c>invOddOddPost</c>, <c>DCCompensate</c>,
/// <c>ClipDCL</c>.</item>
/// </list>
/// The default decode primitives are NOT the structural inverse of the encode
/// ones (jxrlib folds the HST rescaling differently); the <c>*Alt</c> forms are.
/// Correctness here is pinned to matching jxrlib's exact integer output
/// (golden vectors), not to a round-trip assumption.
///
/// <para>Buffer-relative functions (<c>Pre/PostStage1/2</c>) take the working
/// macroblock buffer as a <see cref="Span{Int32}"/> plus the base indices
/// <c>p0</c>/<c>p1</c> of the two ring rows, mirroring jxrlib's pointer
/// arithmetic (<c>p2 = p0 + 72 - iOffset</c>, <c>p3 = p1 + 64 - iOffset</c>).</para>
/// </remarks>
internal static class PhotoOverlapTransform
{
    // strFwdTransform.c:33  ROTATE1(a,b): b -= (a+1)>>1, a += (b+1)>>1
    private static void Rotate1(ref int a, ref int b)
    {
        b -= (a + 1) >> 1;
        a += (b + 1) >> 1;
    }

    // strInvTransform.c:34  IROTATE1(a,b): a -= (b+1)>>1, b += (a+1)>>1
    private static void IRotate1(ref int a, ref int b)
    {
        a -= (b + 1) >> 1;
        b += (a + 1) >> 1;
    }

    // strTransform.c strDCT2x2dn — needed by the Stage*Split butterflies.
    private static void Dct2x2dn(ref int a, ref int b, ref int inC, ref int d)
    {
        a += d;
        b -= inC;
        int t = (a - b) >> 1;
        int c = t - d;
        d = t - inC;
        a -= d;
        b += c;
        inC = c;
    }

    // ============================================================ ENCODE side

    /// <summary>strFwdTransform.c:150 strPre2 — 2-point pre-filter for boundaries.</summary>
    internal static void Pre2(ref int a, ref int b)
    {
        b -= (a + 2) >> 2;
        a -= (b + 1) >> 1;
        a -= b >> 5;
        a -= b >> 9;
        a -= b >> 13;
        b -= (a + 2) >> 2;
    }

    /// <summary>strFwdTransform.c:170 strPre2x2.</summary>
    internal static void Pre2x2(ref int a, ref int b, ref int c, ref int d)
    {
        a += d;
        b += c;
        d -= (a + 1) >> 1;
        c -= (b + 1) >> 1;

        b -= (a + 2) >> 2;
        a -= (b + 1) >> 1;
        a -= b >> 5;
        a -= b >> 9;
        a -= b >> 13;
        b -= (a + 2) >> 2;

        d += (a + 1) >> 1;
        c += (b + 1) >> 1;
        a -= d;
        b -= c;
    }

    /// <summary>strFwdTransform.c:205 strPre4 — 4-point pre-filter for boundaries.</summary>
    internal static void Pre4(ref int a, ref int b, ref int c, ref int d)
    {
        a += d; b += c;
        d -= (a + 1) >> 1; c -= (b + 1) >> 1;

        Rotate1(ref c, ref d);

        HstEnc1Edge(ref a, ref d);
        HstEnc1Edge(ref b, ref c);

        d += (a + 1) >> 1; c += (b + 1) >> 1;
        a -= d; b -= c;
    }

    // strFwdTransform.c:317 strHSTenc — note: loads d=*pc, c=*pd (c/d swapped).
    private static void HstEnc(ref int a, ref int b, ref int pc, ref int pd)
    {
        int d = pc;   // *pc -> d
        int c = pd;   // *pd -> c
        a += c;
        b -= d;
        c = ((a - b) >> 1) - c;
        d += b >> 1;
        b += c;
        a -= (d * 3 + 4) >> 3;
        pc = c;       // store back in original positions
        pd = d;
    }

    // strFwdTransform.c:340 strHSTenc1.
    private static void HstEnc1(ref int a, ref int d)
    {
        d -= a >> 7;
        d += a >> 10;
        d -= (a * 3 + 0) >> 4;
        a -= (d * 3 + 0) >> 3;
        d = (a >> 1) - d;
        a -= d;
    }

    // strFwdTransform.c:360 strHSTenc1_edge — negates *pd on load, 1D scaling.
    private static void HstEnc1Edge(ref int a, ref int pd)
    {
        int d = -pd;  // negative sign needed for the 1D scaling case
        a -= d;
        d += a >> 1;
        a -= (d * 3 + 4) >> 3;
        d -= a >> 7;
        d += a >> 10;
        d -= (a * 3 + 0) >> 4;
        a -= (d * 3 + 0) >> 3;
        d = (a >> 1) - d;
        a -= d;
        pd = d;
    }

    // strFwdTransform.c:418 fwdOddOddPre — Kron(Rotate(pi/8),Rotate(pi/8)) pre variant.
    private static void FwdOddOddPre(ref int a, ref int b, ref int c, ref int d)
    {
        d += a;
        c -= b;
        int t1 = d >> 1; a -= t1;
        int t2 = c >> 1; b += t2;

        a += (b * 3 + 4) >> 3;
        b -= (a * 3 + 2) >> 2;
        a += (b * 3 + 6) >> 3;

        b -= t2; a += t1; c += b; d -= a;
    }

    /// <summary>
    /// strFwdTransform.c:237 strPre4x4Stage1Split — first-level overlap straddling
    /// a 4×4 block boundary, across the two ring rows at base indices p0/p1.
    /// </summary>
    internal static void PreStage1Split(Span<int> buf, int p0, int p1, int iOffset)
    {
        int p2 = p0 + 72 - iOffset;
        int p3 = p1 + 64 - iOffset;
        p0 += 12;
        p1 += 4;

        for (var i = 0; i < 4; i++)
            HstEnc(ref buf[p0 + i], ref buf[p2 + i], ref buf[p1 + i], ref buf[p3 + i]);
        for (var i = 0; i < 4; i++)
            HstEnc1(ref buf[p0 + i], ref buf[p3 + i]);

        Rotate1(ref buf[p1 + 2], ref buf[p1 + 3]);
        Rotate1(ref buf[p1 + 0], ref buf[p1 + 1]);
        Rotate1(ref buf[p2 + 1], ref buf[p2 + 3]);
        Rotate1(ref buf[p2 + 0], ref buf[p2 + 2]);

        FwdOddOddPre(ref buf[p3 + 0], ref buf[p3 + 1], ref buf[p3 + 2], ref buf[p3 + 3]);

        for (var i = 0; i < 4; i++)
            Dct2x2dn(ref buf[p0 + i], ref buf[p2 + i], ref buf[p1 + i], ref buf[p3 + i]);
    }

    /// <summary>strFwdTransform.c:270 strPre4x4Stage1 — single-buffer convenience (p1 = p0 + 16).</summary>
    internal static void PreStage1(Span<int> buf, int p, int iOffset)
        => PreStage1Split(buf, p, p + 16, iOffset);

    /// <summary>strFwdTransform.c:283 strPre4x4Stage2Split — second-level overlap across MB boundaries.</summary>
    internal static void PreStage2Split(Span<int> buf, int p0, int p1)
    {
        HstEnc(ref buf[p0 - 96], ref buf[p0 + 96], ref buf[p1 - 112], ref buf[p1 + 80]);
        HstEnc(ref buf[p0 - 32], ref buf[p0 + 32], ref buf[p1 - 48], ref buf[p1 + 16]);
        HstEnc(ref buf[p0 - 80], ref buf[p0 + 112], ref buf[p1 - 128], ref buf[p1 + 64]);
        HstEnc(ref buf[p0 - 16], ref buf[p0 + 48], ref buf[p1 - 64], ref buf[p1 + 0]);
        HstEnc1(ref buf[p0 - 96], ref buf[p1 + 80]);
        HstEnc1(ref buf[p0 - 32], ref buf[p1 + 16]);
        HstEnc1(ref buf[p0 - 80], ref buf[p1 + 64]);
        HstEnc1(ref buf[p0 - 16], ref buf[p1 + 0]);

        Rotate1(ref buf[p1 - 48], ref buf[p1 - 112]);
        Rotate1(ref buf[p1 - 64], ref buf[p1 - 128]);
        Rotate1(ref buf[p0 + 112], ref buf[p0 + 96]);
        Rotate1(ref buf[p0 + 48], ref buf[p0 + 32]);

        FwdOddOddPre(ref buf[p1 + 0], ref buf[p1 + 64], ref buf[p1 + 16], ref buf[p1 + 80]);

        Dct2x2dn(ref buf[p0 - 96], ref buf[p1 - 112], ref buf[p0 + 96], ref buf[p1 + 80]);
        Dct2x2dn(ref buf[p0 - 32], ref buf[p1 - 48], ref buf[p0 + 32], ref buf[p1 + 16]);
        Dct2x2dn(ref buf[p0 - 80], ref buf[p1 - 128], ref buf[p0 + 112], ref buf[p1 + 64]);
        Dct2x2dn(ref buf[p0 - 16], ref buf[p1 - 64], ref buf[p0 + 48], ref buf[p1 + 0]);
    }

    // ============================================================ DECODE side

    /// <summary>strInvTransform.c:136 strPost2 — default 2-point post (lossy-path operator).</summary>
    internal static void Post2(ref int a, ref int b)
    {
        b += (a + 4) >> 3;
        a += (b + 2) >> 2;
        b += (a + 4) >> 3;
    }

    /// <summary>strInvTransform.c:143 strPost2_alternate — exact inverse of <see cref="Pre2"/>.</summary>
    internal static void Post2Alt(ref int a, ref int b)
    {
        b += (a + 2) >> 2;
        a += (b + 1) >> 1;
        a += b >> 5;
        a += b >> 9;
        a += b >> 13;
        b += (a + 2) >> 2;
    }

    /// <summary>strInvTransform.c:162 strPost2x2.</summary>
    internal static void Post2x2(ref int a, ref int b, ref int c, ref int d)
    {
        a += d;
        b += c;
        d -= (a + 1) >> 1;
        c -= (b + 1) >> 1;

        b += (a + 2) >> 2;
        a += (b + 1) >> 1;
        b += (a + 2) >> 2;

        d += (a + 1) >> 1;
        c += (b + 1) >> 1;
        a -= d;
        b -= c;
    }

    /// <summary>strInvTransform.c:193 strPost2x2_alternate — exact inverse of <see cref="Pre2x2"/>.</summary>
    internal static void Post2x2Alt(ref int a, ref int b, ref int c, ref int d)
    {
        a += d;
        b += c;
        d -= (a + 1) >> 1;
        c -= (b + 1) >> 1;

        b += (a + 2) >> 2;
        a += (b + 1) >> 1;
        a += b >> 5;
        a += b >> 9;
        a += b >> 13;
        b += (a + 2) >> 2;

        d += (a + 1) >> 1;
        c += (b + 1) >> 1;
        a -= d;
        b -= c;
    }

    /// <summary>strInvTransform.c:228 strPost4 — default 4-point post (lossy-path operator).</summary>
    internal static void Post4(ref int a, ref int b, ref int c, ref int d)
    {
        a += d; b += c;
        d -= (a + 1) >> 1; c -= (b + 1) >> 1;

        IRotate1(ref c, ref d);

        d += (a + 1) >> 1; c += (b + 1) >> 1;
        a -= d - ((d * 3 + 16) >> 5); b -= c - ((c * 3 + 16) >> 5);
        d += (a * 3 + 8) >> 4; c += (b * 3 + 8) >> 4;
        a += (d * 3 + 16) >> 5; b += (c * 3 + 16) >> 5;
    }

    /// <summary>strInvTransform.c:252 strPost4_alternate — exact inverse of <see cref="Pre4"/>.</summary>
    internal static void Post4Alt(ref int a, ref int b, ref int c, ref int d)
    {
        a += d; b += c;
        d -= (a + 1) >> 1; c -= (b + 1) >> 1;

        HstDec1Edge(ref a, ref d);
        HstDec1Edge(ref b, ref c);
        IRotate1(ref c, ref d);

        d += (a + 1) >> 1; c += (b + 1) >> 1;
        a -= d; b -= c;
    }

    // strInvTransform.c:283 DCCompensate.
    private static void DcCompensate(ref int a, ref int b, ref int c, ref int d, int iDC)
    {
        iDC >>= 1;
        a -= iDC;
        d -= iDC;
        b += iDC;
        c += iDC;
    }

    // strInvTransform.c:300 ClipDCL.
    private static int ClipDcl(int iDCL, int iAltDCL)
    {
        if (iDCL > 0) return iAltDCL > 0 ? Math.Min(iDCL, iAltDCL) : 0;
        if (iDCL < 0) return iAltDCL < 0 ? Math.Max(iDCL, iAltDCL) : 0;
        return 0;
    }

    // strInvTransform.c:507 strHSTdec1 — default (NOT the exact inverse of HstEnc1).
    private static void HstDec1(ref int a, ref int d)
    {
        a += d;
        d = (a >> 1) - d;
        a += (d * 3 + 0) >> 3;
        d += (a * 3 + 0) >> 4;
    }

    // strInvTransform.c:524 strHSTdec1_alternate — HstDec1 plus the 7/1024 rescaling
    // step (d += a>>7; d -= a>>10), making it the exact inverse of HstEnc1. This is
    // the operator a standard (subversion != 0) codestream's decoder uses.
    private static void HstDec1Alt(ref int a, ref int d)
    {
        a += d;
        d = (a >> 1) - d;
        a += (d * 3 + 0) >> 3;
        d += (a * 3 + 0) >> 4;
        d += a >> 7;
        d -= a >> 10;
    }

    // strInvTransform.c:544 strHSTdec1_edge — negates *pd on store.
    private static void HstDec1Edge(ref int a, ref int pd)
    {
        int d = pd;
        a += d;
        d = (a >> 1) - d;
        a += (d * 3 + 0) >> 3;
        d += (a * 3 + 0) >> 4;
        d += a >> 7;
        d -= a >> 10;
        a += (d * 3 + 4) >> 3;
        d -= a >> 1;
        a += d;
        pd = -d;  // negative sign for the 1D scaling case
    }

    // strInvTransform.c:569 strHSTdec — exact inverse of HstEnc; swaps c/d on store.
    private static void HstDec(ref int pa, ref int pb, ref int pc, ref int pd)
    {
        int a = pa, b = pb, c = pc, d = pd;
        b -= c;
        a += (d * 3 + 4) >> 3;
        d -= b >> 1;
        c = ((a - b) >> 1) - c;
        pc = d;          // *pc = d
        pd = c;          // *pd = c
        pa = a - c;      // *pa = a - c (new c)
        pb = b + d;      // *pb = b + d (new d)
    }

    // strInvTransform.c:622 invOddOddPost — no sign flips (unlike invOddOdd).
    private static void InvOddOddPost(ref int a, ref int b, ref int c, ref int d)
    {
        d += a;
        c -= b;
        int t1 = d >> 1; a -= t1;
        int t2 = c >> 1; b += t2;

        a -= (b * 3 + 6) >> 3;
        b += (a * 3 + 2) >> 2;
        a -= (b * 3 + 4) >> 3;

        b -= t2; a += t1; c += b; d -= a;
    }

    /// <summary>
    /// strInvTransform.c:318 strPost4x4Stage1Split — inverse of <see cref="PreStage1Split"/>,
    /// including the decoder-only DC-leak compensation (fires only when HP is absent
    /// or the HP quantizer is coarse: <c>(|iDCL| &lt; iHPQP &amp;&amp; iHPQP &gt; 20) || bHPAbsent</c>).
    /// </summary>
    internal static void PostStage1Split(Span<int> buf, int p0, int p1, int iOffset, int iHpQp, bool hpAbsent)
    {
        int p2 = p0 + 72 - iOffset;
        int p3 = p1 + 64 - iOffset;
        p0 += 12;
        p1 += 4;

        for (var i = 0; i < 4; i++)
            Dct2x2dn(ref buf[p0 + i], ref buf[p2 + i], ref buf[p1 + i], ref buf[p3 + i]);

        InvOddOddPost(ref buf[p3 + 0], ref buf[p3 + 1], ref buf[p3 + 2], ref buf[p3 + 3]);

        IRotate1(ref buf[p1 + 2], ref buf[p1 + 3]);
        IRotate1(ref buf[p1 + 0], ref buf[p1 + 1]);
        IRotate1(ref buf[p2 + 1], ref buf[p2 + 3]);
        IRotate1(ref buf[p2 + 0], ref buf[p2 + 2]);

        for (var i = 0; i < 4; i++)
            HstDec1(ref buf[p0 + i], ref buf[p3 + i]);
        for (var i = 0; i < 4; i++)
            HstDec(ref buf[p0 + i], ref buf[p2 + i], ref buf[p1 + i], ref buf[p3 + i]);

        // DC-leak compensation (approx 27/5947 ~ 595/2^17).
        for (var i = 0; i < 4; i++)
        {
            int sum = buf[p0 + i] + buf[p1 + i] + buf[p2 + i] + buf[p3 + i];
            int iTmp = sum >> 1;
            int iDCL = (iTmp * 595 + 65536) >> 17;
            if ((Math.Abs(iDCL) < iHpQp && iHpQp > 20) || hpAbsent)
            {
                int iDclAlt = (buf[p0 + i] - buf[p1 + i] - buf[p2 + i] + buf[p3 + i]) >> 1;
                iDCL = ClipDcl(iDCL, iDclAlt);
                DcCompensate(ref buf[p0 + i], ref buf[p2 + i], ref buf[p1 + i], ref buf[p3 + i], iDCL);
            }
        }
    }

    /// <summary>strInvTransform.c:384 strPost4x4Stage1 — single-buffer convenience.</summary>
    internal static void PostStage1(Span<int> buf, int p, int iOffset, int iHpQp, bool hpAbsent)
        => PostStage1Split(buf, p, p + 16, iOffset, iHpQp, hpAbsent);

    /// <summary>
    /// strInvTransform.c:389 strPost4x4Stage1Split_alternate — the exact structural
    /// inverse of <see cref="PreStage1Split"/> (uses <see cref="HstDec1Alt"/> and has
    /// no DC-leak compensation). This is the operator a standard codestream's decoder
    /// uses (subversion != 0 ⇒ <c>invTransformMacroblock_alteredOperators_hard</c>).
    /// </summary>
    internal static void PostStage1SplitAlt(Span<int> buf, int p0, int p1, int iOffset)
    {
        int p2 = p0 + 72 - iOffset;
        int p3 = p1 + 64 - iOffset;
        p0 += 12;
        p1 += 4;

        for (var i = 0; i < 4; i++)
            Dct2x2dn(ref buf[p0 + i], ref buf[p2 + i], ref buf[p1 + i], ref buf[p3 + i]);

        InvOddOddPost(ref buf[p3 + 0], ref buf[p3 + 1], ref buf[p3 + 2], ref buf[p3 + 3]);

        IRotate1(ref buf[p1 + 2], ref buf[p1 + 3]);
        IRotate1(ref buf[p1 + 0], ref buf[p1 + 1]);
        IRotate1(ref buf[p2 + 1], ref buf[p2 + 3]);
        IRotate1(ref buf[p2 + 0], ref buf[p2 + 2]);

        for (var i = 0; i < 4; i++)
            HstDec1Alt(ref buf[p0 + i], ref buf[p3 + i]);
        for (var i = 0; i < 4; i++)
            HstDec(ref buf[p0 + i], ref buf[p2 + i], ref buf[p1 + i], ref buf[p3 + i]);
    }

    /// <summary>strInvTransform.c:422 strPost4x4Stage1_alternate — single-buffer convenience.</summary>
    internal static void PostStage1Alt(Span<int> buf, int p, int iOffset)
        => PostStage1SplitAlt(buf, p, p + 16, iOffset);

    /// <summary>strInvTransform.c:444 strPost4x4Stage2Split — inverse of <see cref="PreStage2Split"/>.</summary>
    internal static void PostStage2Split(Span<int> buf, int p0, int p1)
    {
        Dct2x2dn(ref buf[p0 - 96], ref buf[p0 + 96], ref buf[p1 - 112], ref buf[p1 + 80]);
        Dct2x2dn(ref buf[p0 - 32], ref buf[p0 + 32], ref buf[p1 - 48], ref buf[p1 + 16]);
        Dct2x2dn(ref buf[p0 - 80], ref buf[p0 + 112], ref buf[p1 - 128], ref buf[p1 + 64]);
        Dct2x2dn(ref buf[p0 - 16], ref buf[p0 + 48], ref buf[p1 - 64], ref buf[p1 + 0]);

        InvOddOddPost(ref buf[p1 + 0], ref buf[p1 + 64], ref buf[p1 + 16], ref buf[p1 + 80]);

        IRotate1(ref buf[p0 + 48], ref buf[p0 + 32]);
        IRotate1(ref buf[p0 + 112], ref buf[p0 + 96]);
        IRotate1(ref buf[p1 - 64], ref buf[p1 - 128]);
        IRotate1(ref buf[p1 - 48], ref buf[p1 - 112]);

        HstDec1(ref buf[p0 - 96], ref buf[p1 + 80]);
        HstDec1(ref buf[p0 - 32], ref buf[p1 + 16]);
        HstDec1(ref buf[p0 - 80], ref buf[p1 + 64]);
        HstDec1(ref buf[p0 - 16], ref buf[p1 + 0]);

        HstDec(ref buf[p0 - 96], ref buf[p1 - 112], ref buf[p0 + 96], ref buf[p1 + 80]);
        HstDec(ref buf[p0 - 32], ref buf[p1 - 48], ref buf[p0 + 32], ref buf[p1 + 16]);
        HstDec(ref buf[p0 - 80], ref buf[p1 - 128], ref buf[p0 + 112], ref buf[p1 + 64]);
        HstDec(ref buf[p0 - 16], ref buf[p1 - 64], ref buf[p0 + 48], ref buf[p1 + 0]);
    }

    /// <summary>
    /// strInvTransform.c:473 strPost4x4Stage2Split_alternate — the exact structural
    /// inverse of <see cref="PreStage2Split"/> (identical to <see cref="PostStage2Split"/>
    /// except it uses <see cref="HstDec1Alt"/>). The operator a standard codestream's
    /// decoder uses for the second-level (MB-boundary) overlap.
    /// </summary>
    internal static void PostStage2SplitAlt(Span<int> buf, int p0, int p1)
    {
        Dct2x2dn(ref buf[p0 - 96], ref buf[p0 + 96], ref buf[p1 - 112], ref buf[p1 + 80]);
        Dct2x2dn(ref buf[p0 - 32], ref buf[p0 + 32], ref buf[p1 - 48], ref buf[p1 + 16]);
        Dct2x2dn(ref buf[p0 - 80], ref buf[p0 + 112], ref buf[p1 - 128], ref buf[p1 + 64]);
        Dct2x2dn(ref buf[p0 - 16], ref buf[p0 + 48], ref buf[p1 - 64], ref buf[p1 + 0]);

        InvOddOddPost(ref buf[p1 + 0], ref buf[p1 + 64], ref buf[p1 + 16], ref buf[p1 + 80]);

        IRotate1(ref buf[p0 + 48], ref buf[p0 + 32]);
        IRotate1(ref buf[p0 + 112], ref buf[p0 + 96]);
        IRotate1(ref buf[p1 - 64], ref buf[p1 - 128]);
        IRotate1(ref buf[p1 - 48], ref buf[p1 - 112]);

        HstDec1Alt(ref buf[p0 - 96], ref buf[p1 + 80]);
        HstDec1Alt(ref buf[p0 - 32], ref buf[p1 + 16]);
        HstDec1Alt(ref buf[p0 - 80], ref buf[p1 + 64]);
        HstDec1Alt(ref buf[p0 - 16], ref buf[p1 + 0]);

        HstDec(ref buf[p0 - 96], ref buf[p1 - 112], ref buf[p0 + 96], ref buf[p1 + 80]);
        HstDec(ref buf[p0 - 32], ref buf[p1 - 48], ref buf[p0 + 32], ref buf[p1 + 16]);
        HstDec(ref buf[p0 - 80], ref buf[p1 - 128], ref buf[p0 + 112], ref buf[p1 + 64]);
        HstDec(ref buf[p0 - 16], ref buf[p1 - 64], ref buf[p0 + 48], ref buf[p1 + 0]);
    }
}
