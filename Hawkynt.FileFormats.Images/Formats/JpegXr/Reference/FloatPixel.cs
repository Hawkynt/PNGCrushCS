namespace SharpAstro.Jxr;

/// <summary>
/// JPEG XR's custom-float ⟷ internal-integer ("PixelI") conversion, ported from
/// jxrlib <c>float2pixel</c> (strenc.c) / <c>pixel2float</c> (strdec.c). A 32-bit
/// IEEE float is mapped to a sign-magnitude integer with a chosen exponent bias
/// (<paramref name="c"/>) and mantissa length (<paramref name="lm"/> bits). The
/// mapping <b>quantizes the mantissa to <c>lm</c> bits</b>, so it is lossy at the
/// float level unless the source already fits — that precision/size trade-off is the
/// point of BD32F/BD16F. The codec then losslessly preserves the integer value, so
/// <see cref="ToFloat"/>∘<see cref="ToPixel"/> is idempotent and a codec round-trip
/// reproduces it exactly.
/// </summary>
internal static class FloatPixel
{
    /// <summary>jxrlib <c>float2pixel</c> — IEEE float → internal PixelI (sign-magnitude,
    /// exponent bias <paramref name="c"/>, <paramref name="lm"/>-bit mantissa).</summary>
    public static int ToPixel(float f, int c, int lm)
    {
        if (f == 0) return 0;

        int xi = BitConverter.SingleToInt32Bits(f);
        int e = (xi >> 23) & 0xff;            // biased exponent (sign sits above in xi)
        int m = (xi & 0x7fffff) | 0x800000;   // mantissa with the implicit normalizer bit
        if (e == 0) { m ^= 0x800000; e++; }   // denormal: drop normalizer, exponent = -126

        int e1 = e - 127 + c;                 // re-bias toward the chosen exponent
        if (e1 <= 1)
        {
            if (e1 < 1) m >>= (1 - e1);       // shift mantissa right to make exponent 1
            e1 = 1;
            if ((m & 0x800000) == 0) e1 = 0;  // still denormal ⇒ exponent 0
        }
        m &= 0x7fffff;

        // take the 23-bit mantissa, round to lm bits.
        int h = (e1 << lm) + ((m + (1 << (23 - lm - 1))) >> (23 - lm));
        int s = xi >> 31;                     // 0 or -1
        return (h ^ s) - s;                   // apply sign
    }

    /// <summary>jxrlib <c>pixel2float</c> — internal PixelI → IEEE float (exact inverse of
    /// <see cref="ToPixel"/> for representable values).</summary>
    public static float ToFloat(int h, int c, int lm)
    {
        int lmshift = 1 << lm;
        int s = h >> 31;                      // 0 or -1
        int abs = (h ^ s) - s;

        int e = (int)((uint)abs >> lm);
        int m = (abs & (lmshift - 1)) | lmshift; // mantissa with normalizer
        if (e == 0) { m ^= lmshift; e = 1; }     // denormal

        e += (127 - c);
        while (m < lmshift && e > 1 && m > 0) { e--; m <<= 1; } // try to normalize
        if (m < lmshift) e = 0; else m ^= lmshift;               // truly denormal ⇒ exp 0
        m <<= (23 - lm);

        int xi = (s & unchecked((int)0x80000000)) | (e << 23) | m;
        return BitConverter.Int32BitsToSingle(xi);
    }

    /// <summary>Quantize a float through the BD32F/BD16F representation (<c>ToFloat∘ToPixel</c>) —
    /// the value a lossless codec round-trip reproduces.</summary>
    public static float Requantize(float f, int c, int lm) => ToFloat(ToPixel(f, c, lm), c, lm);

    // ---------------------------------------------------------------- half (BD16F)
    // jxrlib forwardHalf (strenc.c:400) / backwardHalf (strdec.c:504): the half-float is kept as
    // its raw sign-magnitude bit pattern — the 15-bit exponent+mantissa magnitude with the sign
    // applied as integer negation. No value decoding, so it is EXACTLY lossless (no mantissa
    // quantization, unlike BD32F): the codec preserves the integer, round-tripping the half bits.

    /// <summary>jxrlib <c>forwardHalf</c> — IEEE half (sign bit 15) → sign-magnitude internal PixelI.</summary>
    public static int HalfToPixel(Half h)
    {
        int v = BitConverter.HalfToInt16Bits(h); // signed: negative when the sign bit is set
        int s = v >> 31;
        return ((v & 0x7fff) ^ s) - s;
    }

    /// <summary>jxrlib <c>backwardHalf</c> — sign-magnitude internal PixelI → IEEE half (exact inverse).</summary>
    public static Half PixelToHalf(int p)
    {
        int s = p >> 31;
        int bits = ((p & 0x7fff) ^ s) - s;
        return BitConverter.Int16BitsToHalf((short)bits);
    }
}
