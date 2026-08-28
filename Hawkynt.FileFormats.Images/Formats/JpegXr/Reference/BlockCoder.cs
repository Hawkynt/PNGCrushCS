namespace SharpAstro.Jxr;

/// <summary>
/// The adaptive-Huffman contexts a single coefficient block needs (jxrlib's
/// <c>pAHexpt</c> slots, for one channel group): the joint FIRST_INDEX symbol
/// (alphabet 12), the two continuity-conditioned INDEX symbols (alphabet 6), the
/// run code (alphabet 5), and the two continuity-conditioned abs-level codes
/// (alphabet 7). Each is created at its initial table.
/// </summary>
internal sealed class BlockContext
{
    public readonly AdaptiveHuffman First;
    public readonly AdaptiveHuffman Run;
    public readonly AdaptiveHuffman[] Index;     // [iCont]
    public readonly AdaptiveHuffman[] AbsLevel;  // [iCont]

    public BlockContext()
    {
        First = Make(12);
        Run = Make(5);
        Index = new[] { Make(6), Make(6) };
        AbsLevel = new[] { Make(7), Make(7) };
    }

    private static AdaptiveHuffman Make(int nSym)
    {
        var h = new AdaptiveHuffman(nSym);
        h.AdaptDiscriminant(); // seed the initial table
        return h;
    }
}

/// <summary>
/// Per-block run/level coefficient coder, ported from jxrlib's
/// <c>EncodeBlock</c> / <c>DecodeBlock</c> (segenc.c / segdec.c). A block is a
/// list of (run, level) pairs: <c>coef[2k]</c> = zero run before the k-th
/// nonzero, <c>coef[2k+1]</c> = its signed level. The first symbol is a joint
/// FIRST_INDEX (SRn·4 + SL·2 + SR); subsequent ones are INDEX (SRn·2 + SL). The
/// level sign rides as the LSB of the index VLC; magnitudes &gt; 1 use abs-level
/// coding; nonzero runs use run coding. Encode∘decode reproduces the pairs.
/// </summary>
internal static class BlockCoder
{
    private static int SL(int level) => (uint)(level + 1) > 2u ? 1 : 0; // |level| > 1

    private static void EncodeFirstIndex(BitWriter w, BlockContext ctx, int index, bool sign)
    {
        ctx.First.Discriminant += ctx.First.Delta(index);
        ctx.First.Discriminant1 += ctx.First.Delta1(index);
        ctx.First.Encode(w, index);
        w.WriteBit(sign);
    }

    private static void EncodeIndex(BitWriter w, BlockContext ctx, int iCont, int iLoc, int index, bool sign)
    {
        if (iLoc < 15)
        {
            var ah = ctx.Index[iCont];
            ah.Discriminant += ah.Delta(index);
            ah.Discriminant1 += ah.Delta1(index);
            ah.Encode(w, index);
            w.WriteBit(sign);
        }
        else if (iLoc == 15)
        {
            ReadOnlySpan<int> gCode = stackalloc[] { 0, 6, 2, 7 };
            ReadOnlySpan<int> gLen = stackalloc[] { 1, 3, 2, 3 };
            w.WriteBits((uint)(gCode[index] * 2 + (sign ? 1 : 0)), gLen[index] + 1);
        }
        else // iLoc >= 16: deterministic single SL bit
        {
            w.WriteBits((uint)(index * 2 + (sign ? 1 : 0)), 2);
        }
    }

    /// <summary>Encode <paramref name="numNonzero"/> (run, level) pairs starting at scan position <paramref name="iLocation"/>.</summary>
    public static void Encode(BitWriter w, ReadOnlySpan<int> coef, int numNonzero, int iLocation, BlockContext ctx)
    {
        int iLev = coef[1];
        int iSR = coef[0] == 0 ? 1 : 0;
        int iSL = SL(iLev);
        int iSRn = numNonzero == 1 ? 0 : (coef[2] > 0 ? 2 : 1);

        EncodeFirstIndex(w, ctx, iSRn * 4 + iSL * 2 + iSR, iLev < 0);
        int iCont = iSR & iSRn;
        if (iSL != 0) CoefficientSyntax.EncodeAbsLevel(w, ctx.AbsLevel[iCont], Math.Abs(iLev));
        if (iSR == 0) CoefficientSyntax.EncodeRun(w, ctx.Run, coef[0], 15 - iLocation);
        iLocation += coef[0] + 1;

        for (var k = 1; k < numNonzero; k++)
        {
            if (iSRn == 2) CoefficientSyntax.EncodeRun(w, ctx.Run, coef[k * 2], 15 - iLocation);
            iLocation += coef[k * 2] + 1;
            iSRn = k == numNonzero - 1 ? 0 : (coef[k * 2 + 2] > 0 ? 2 : 1);
            iLev = coef[k * 2 + 1];
            iSL = SL(iLev);
            EncodeIndex(w, ctx, iCont, iLocation, iSRn * 2 + iSL, iLev < 0);
            iCont &= iSRn;
            if (iSL != 0) CoefficientSyntax.EncodeAbsLevel(w, ctx.AbsLevel[iCont], Math.Abs(iLev));
        }
    }

    private static int DecodeFirstIndex(ref BitReader r, BlockContext ctx)
    {
        int idx = ctx.First.Decode(ref r);
        ctx.First.Discriminant += ctx.First.Delta(idx);
        ctx.First.Discriminant1 += ctx.First.Delta1(idx);
        return idx;
    }

    private static int DecodeIndex(ref BitReader r, BlockContext ctx, int iCont, int iLoc)
    {
        if (iLoc < 15)
        {
            var ah = ctx.Index[iCont];
            int idx = ah.Decode(ref r);
            ah.Discriminant += ah.Delta(idx);
            ah.Discriminant1 += ah.Delta1(idx);
            return idx;
        }
        if (iLoc == 15)
        {
            if (!r.ReadBit()) return 0;
            if (!r.ReadBit()) return 2;
            return 1 + 2 * (r.ReadBit() ? 1 : 0);
        }
        return r.ReadBit() ? 1 : 0;
    }

    /// <summary>Decode (run, level) pairs into <paramref name="coef"/>; returns the count of nonzeros.</summary>
    public static int Decode(ref BitReader r, Span<int> coef, int iLocation, BlockContext ctx)
    {
        int numNonzero = 1;
        int iIndex = DecodeFirstIndex(ref r, ctx);
        int iSR = iIndex & 1;
        int iSRn = iIndex >> 2;
        int iCont = iSR & iSRn;
        int iSign = r.ReadBit() ? -1 : 0;

        coef[1] = (iIndex & 2) != 0
            ? (CoefficientSyntax.DecodeAbsLevel(ref r, ctx.AbsLevel[iCont]) ^ iSign) - iSign
            : 1 | iSign;
        coef[0] = iSR == 0 ? CoefficientSyntax.DecodeRun(ref r, ctx.Run, 15 - iLocation) : 0;
        iLocation += coef[0] + 1;

        while (iSRn != 0)
        {
            iSR = iSRn & 1;
            coef[numNonzero * 2] = iSR == 0 ? CoefficientSyntax.DecodeRun(ref r, ctx.Run, 15 - iLocation) : 0;
            iLocation += coef[numNonzero * 2] + 1;

            iIndex = DecodeIndex(ref r, ctx, iCont, iLocation);
            iSRn = iIndex >> 1;
            iCont &= iSRn;
            iSign = r.ReadBit() ? -1 : 0;

            coef[numNonzero * 2 + 1] = (iIndex & 1) != 0
                ? (CoefficientSyntax.DecodeAbsLevel(ref r, ctx.AbsLevel[iCont]) ^ iSign) - iSign
                : 1 | iSign;
            numNonzero++;
        }
        return numNonzero;
    }
}
