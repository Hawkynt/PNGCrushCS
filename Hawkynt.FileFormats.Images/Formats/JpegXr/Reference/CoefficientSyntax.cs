namespace SharpAstro.Jxr;

/// <summary>
/// The leaf coefficient-syntax coders: significant <b>run</b> length and
/// significant <b>absolute level</b>. Ported from jxrlib's
/// <c>EncodeSignificantRun</c> / <c>DecodeSignificantRun</c> and
/// <c>EncodeSignificantAbsLevel</c> / <c>DecodeSignificantAbsLevel</c>
/// (segenc.c / segdec.c). Each is a small VLC (via <see cref="AdaptiveHuffman"/>)
/// plus a fixed-length-code (FLC) refinement.
/// </summary>
internal static class CoefficientSyntax
{
    // image.c gSignificantRunBin / gSignificantRunFixedLength (indexed by maxRun / by table+bin).
    private static readonly int[] RunBin = { -1, -1, -1, -1, 2, 2, 2, 1, 1, 1, 1, 0, 0, 0, 0 };
    private static readonly int[] RunFixedLength = { 0, 0, 1, 1, 3, 0, 0, 1, 1, 2, 0, 0, 0, 0, 1 };
    // segenc EncodeSignificantRun aIndex (encode: run+bin*14-1 -> symbol).
    private static readonly int[] RunIndexEnc =
    {
        0,1,2,2,3,3,4,4,4,4,4,4,4,4,
        0,1,2,2,3,3,4,4,4,4,0,0,0,0,
        0,1,2,3,4,4,
    };
    // segdec DecodeSignificantRun aRemap (decode: symbol+bin*5 -> base run).
    private static readonly int[] RunRemapDec = { 1, 2, 3, 5, 7, 1, 2, 3, 5, 7, 1, 2, 3, 4, 5 };
    private static readonly int[] RunShortLen = { 3, 3, 2, 1 };

    // segenc EncodeSignificantAbsLevel aIndex/aFixedLength (encode).
    private static readonly int[] AbsIndexEnc = { 0, 1, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5 };
    private static readonly int[] AbsFixedLen = { 0, 0, 1, 2, 2, 2 };
    // segdec DecodeSignificantAbsLevel aRemap (decode: symbol -> base level).
    private static readonly int[] AbsRemapDec = { 2, 3, 4, 6, 10, 14 };

    // ----------------------------------------------------------- run

    /// <summary>Encode a significant run (1-based) given the max possible run. <paramref name="ah5"/> is the run VLC (alphabet 5, table 0).</summary>
    public static void EncodeRun(BitWriter w, AdaptiveHuffman ah5, int run, int maxRun)
    {
        if (maxRun < 5)
        {
            if (maxRun > 1)
                w.WriteBits((uint)(maxRun != run ? 1 : 0), RunShortLen[maxRun - run] - (4 - maxRun));
            return;
        }
        int bin = RunBin[maxRun];
        int index = RunIndexEnc[run + bin * 14 - 1];
        int flc = RunFixedLength[index + bin * 5];
        ah5.Encode(w, index);            // run VLC always uses table 0; no discriminant update
        w.WriteBits((uint)(run + 1), flc);
    }

    /// <summary>Decode a significant run given the max possible run.</summary>
    public static int DecodeRun(ref BitReader r, AdaptiveHuffman ah5, int maxRun)
    {
        if (maxRun < 5)
        {
            if (maxRun == 1) return 1;
            if (r.ReadBit()) return 1;
            if (maxRun == 2 || r.ReadBit()) return 2;
            if (maxRun == 3 || r.ReadBit()) return 3;
            return 4;
        }
        int bin = RunBin[maxRun];
        int index = ah5.Decode(ref r) + bin * 5;
        int run = RunRemapDec[index];
        int flc = RunFixedLength[index];
        if (flc != 0) run += (int)r.ReadBits(flc);
        return run;
    }

    // ----------------------------------------------------------- abs level

    /// <summary>
    /// Encode an absolute coefficient level (<paramref name="absLevel"/> = |level|, ≥ 2).
    /// <paramref name="ah7"/> is the level VLC (alphabet 7). Mirrors jxrlib calling
    /// <c>EncodeSignificantAbsLevel(|level| - 1)</c>.
    /// </summary>
    public static void EncodeAbsLevel(BitWriter w, AdaptiveHuffman ah7, int absLevel)
    {
        int a = absLevel - 2; // jxrlib: arg = |level|-1, then arg--
        if (a >= 16)
        {
            int i = a >> 5, iFixed = 4;
            while (i != 0) { iFixed++; i >>= 1; }
            ah7.Discriminant += ah7.Delta(6); // jxrlib EncodeSignificantAbsLevel updates the discriminant
            ah7.Encode(w, 6);
            if (iFixed > 18)
            {
                w.WriteBits(15, 4);
                if (iFixed > 21) { w.WriteBits(3, 2); w.WriteBits((uint)(iFixed - 22), 3); }
                else w.WriteBits((uint)(iFixed - 19), 2);
            }
            else
            {
                w.WriteBits((uint)(iFixed - 4), 4);
            }
            w.WriteBits((uint)(a & ((1 << iFixed) - 1)), iFixed);
        }
        else
        {
            int index = AbsIndexEnc[a];
            int iFixed = AbsFixedLen[index];
            ah7.Discriminant += ah7.Delta(index); // jxrlib EncodeSignificantAbsLevel updates the discriminant
            ah7.Encode(w, index);
            w.WriteBits((uint)(a & ((1 << iFixed) - 1)), iFixed);
        }
    }

    /// <summary>Decode an absolute coefficient level (returns |level|, ≥ 2).</summary>
    public static int DecodeAbsLevel(ref BitReader r, AdaptiveHuffman ah7)
    {
        int index = ah7.Decode(ref r);
        ah7.Discriminant += ah7.Delta(index); // jxrlib DecodeSignificantAbsLevel updates the discriminant
        if (index < 2) return index + 2;
        if (index < 6) return AbsRemapDec[index] + (int)r.ReadBits(AbsFixedLen[index]);

        int iFixed = (int)r.ReadBits(4) + 4;
        if (iFixed == 19)
        {
            iFixed += (int)r.ReadBits(2);
            if (iFixed == 22) iFixed += (int)r.ReadBits(3);
        }
        return 2 + (1 << iFixed) + (int)r.ReadBits(iFixed);
    }
}
