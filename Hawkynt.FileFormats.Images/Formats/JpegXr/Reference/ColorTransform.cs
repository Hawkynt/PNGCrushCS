namespace SharpAstro.Jxr;

/// <summary>
/// The JPEG XR internal color transform — a reversible integer lifting between
/// RGB and the codec's internal YUV (used for <c>INTERNAL_CLR_FMT = YUV444</c>,
/// which is what Windows Photo / WIC expects for RGB JXR). Ported from jxrlib's
/// <c>_CC</c> (encode, image/encode/strenc.c) and <c>_ICC</c> (decode,
/// image/decode/strdec.c) macros, plus the CMYK variants.
/// </summary>
/// <remarks>
/// This is jxrlib's specific 3-lift transform, NOT classical YCoCg-R. Forward
/// then inverse is bit-exact identity. The transform operates in place on the
/// (r, g, b) triple; channel 0 (g after the forward lift) carries luma.
/// </remarks>
internal static class ColorTransform
{
    /// <summary>strenc.c:414 _CC — forward RGB → internal YUV.</summary>
    public static void ForwardRgb(ref int r, ref int g, ref int b)
    {
        b -= r;
        r += ((b + 1) >> 1) - g;
        g += r >> 1;
    }

    /// <summary>strdec.c:404 _ICC — inverse internal YUV → RGB.</summary>
    public static void InverseRgb(ref int r, ref int g, ref int b)
    {
        g -= r >> 1;
        r -= ((b + 1) >> 1) - g;
        b += r;
    }

    /// <summary>strenc.c:415 _CC_CMYK — forward CMYK transform.</summary>
    public static void ForwardCmyk(ref int c, ref int m, ref int y, ref int k)
    {
        y -= c;
        c += ((y + 1) >> 1) - m;
        m += (c >> 1) - k;
        k += (m + 1) >> 1;
    }

    /// <summary>strdec.c:405 _ICC_CMYK — inverse CMYK transform.</summary>
    public static void InverseCmyk(ref int c, ref int m, ref int y, ref int k)
    {
        k -= (m + 1) >> 1;
        m -= (c >> 1) - k;
        c -= ((y + 1) >> 1) - m;
        y += c;
    }
}
