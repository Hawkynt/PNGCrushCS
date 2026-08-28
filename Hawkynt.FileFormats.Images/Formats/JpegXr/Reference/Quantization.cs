namespace SharpAstro.Jxr;

/// <summary>
/// A resolved per-band quantizer: the QP index expanded into the step size and
/// the reciprocal-multiply parameters used by the forward quantizer. Mirrors
/// jxrlib's <c>CWMIQuantizer</c> (image/sys/strcodec.h).
/// </summary>
internal struct JxrQuantizer
{
    public int Index;   // iIndex (0..255)
    public int Qp;      // iQP — the dequant step size (dequant = raw * Qp)
    public int Offset;  // iOffset — rounding offset added to |coeff| before scaling
    public uint Man;    // iMan — reciprocal mantissa (0 ⇒ power-of-two / shift-only path)
    public int Exp;     // iExp — right-shift after the high-multiply (or the shift for the mulless path)
}

/// <summary>
/// JPEG XR quantization, ported faithfully from jxrlib:
/// <list type="bullet">
/// <item><c>image/sys/strPredQuant.c</c> — <c>remapQP</c> + the 32-entry
/// reciprocal table <c>gs_QPRecipTable</c>.</item>
/// <item><c>image/encode/strPredQuantEnc.c</c> — <c>QUANT</c>, <c>QUANT_Mulless</c>,
/// <c>MUL32HR</c>.</item>
/// <item><c>image/decode/strPredQuantDec.c</c> — <c>DEQUANT</c> (just <c>raw * iQP</c>).</item>
/// </list>
/// Quantization is sign-magnitude: the magnitude is scaled by a 32-bit reciprocal
/// multiply (or a right shift when the step is a power of two) and the sign is
/// reattached. QP index 0 (and, in non-scaled mode, indices 1–4) is lossless
/// (<c>Qp = 1</c>) and round-trips exactly.
/// </summary>
internal static class Quantization
{
    // common.h
    private const int ShiftZero = 1;
    private const int QpFracBits = 2;

    // strPredQuant.c:40 gs_QPRecipTable[32] — (reciprocal mantissa, exponent).
    // A 0 mantissa marks a power-of-two step (the shift-only QUANT_Mulless path).
    private static readonly (uint Man, int Exp)[] QpRecipTable =
    {
        (0x00000000u, 0), // 0 (invalid)
        (0x00000000u, 0), // 1 (lossless)
        (0x00000000u, 1), // 2
        (0xaaaaaaabu, 1),
        (0x00000000u, 2), // 4
        (0xcccccccdu, 2),
        (0xaaaaaaabu, 2),
        (0x92492493u, 2),
        (0x00000000u, 3), // 8
        (0xe38e38e4u, 3),
        (0xcccccccdu, 3),
        (0xba2e8ba3u, 3),
        (0xaaaaaaabu, 3),
        (0x9d89d89eu, 3),
        (0x92492493u, 3),
        (0x88888889u, 3),
        (0x00000000u, 4), // 16
        (0xf0f0f0f1u, 4),
        (0xe38e38e4u, 4),
        (0xd79435e6u, 4),
        (0xcccccccdu, 4),
        (0xc30c30c4u, 4),
        (0xba2e8ba3u, 4),
        (0xb21642c9u, 4),
        (0xaaaaaaabu, 4),
        (0xa3d70a3eu, 4),
        (0x9d89d89eu, 4),
        (0x97b425eeu, 4),
        (0x92492493u, 4),
        (0x8d3dcb09u, 4),
        (0x88888889u, 4),
        (0x84210843u, 4),
    };

    /// <summary>
    /// strPredQuant.c:79 remapQP — expand the QP index into step + reciprocal params.
    /// <paramref name="shift"/> is only consulted in scaled-arithmetic mode (it is
    /// <c>SHIFTZERO</c>, or <c>SHIFTZERO-1</c> for shifted UV channels).
    /// </summary>
    public static void RemapQp(ref JxrQuantizer q, int shift, bool scaledArith)
    {
        int idx = q.Index;

        if (idx == 0) // lossless
        {
            q.Qp = 1; q.Man = 0; q.Exp = 0; q.Offset = 0;
            return;
        }

        int man, exp;
        if (!scaledArith)
        {
            const int ciShift = ShiftZero - (ShiftZero + QpFracBits); // == -QPFRACBITS
            if (idx < 32) { man = (idx + 3) >> 2; exp = ciShift + 2; }
            else if (idx < 48) { man = (16 + (idx & 0xf) + 1) >> 1; exp = ((idx >> 4) - 1) + 1 + ciShift; }
            else { man = 16 + (idx & 0xf); exp = ((idx >> 4) - 1) + ciShift; }
        }
        else
        {
            if (idx < 16) { man = idx; exp = shift; }
            else { man = 16 + (idx & 0xf); exp = ((idx >> 4) - 1) + shift; }
        }

        q.Qp = man << exp;
        q.Man = QpRecipTable[man].Man;
        q.Exp = QpRecipTable[man].Exp + exp;
        q.Offset = (q.Qp * 3 + 1) >> 3;
    }

    /// <summary>Convenience: resolve a quantizer from an index in the default (non-scaled) mode.</summary>
    public static JxrQuantizer Resolve(int index, bool scaledArith = false, int shift = ShiftZero)
    {
        var q = new JxrQuantizer { Index = index };
        RemapQp(ref q, shift, scaledArith);
        return q;
    }

    // strPredQuantEnc.c:41 MUL32HR — high 32 bits of a*b, then a right shift.
    private static int Mul32Hr(uint a, uint b, int r)
        => (int)((uint)((ulong)a * b >> 32) >> r);

    /// <summary>strPredQuantEnc.c:48 QUANT — sign-magnitude reciprocal-multiply quantize.</summary>
    public static int Quant(int v, int offset, uint man, int exp)
    {
        int m = v >> 31; // sign mask: 0 for v>=0, -1 for v<0
        uint magnitude = (uint)((v ^ m) - m + offset);
        return (Mul32Hr(magnitude, man, exp) ^ m) - m;
    }

    /// <summary>strPredQuantEnc.c:32 QUANT_Mulless — sign-magnitude power-of-two (shift) quantize.</summary>
    public static int QuantMulless(int v, int offset, int r)
    {
        int m = v >> 31;
        return ((((v ^ m) - m + offset) >> r) ^ m) - m;
    }

    /// <summary>Quantize one coefficient with a resolved quantizer (picks the mulless or multiply path).</summary>
    public static int Quantize(int v, in JxrQuantizer q)
        => q.Man == 0 ? QuantMulless(v, q.Offset, q.Exp) : Quant(v, q.Offset, q.Man, q.Exp);

    /// <summary>strPredQuantDec.c:30 DEQUANT — reconstruct a coefficient (just <c>raw * Qp</c>).</summary>
    public static int Dequantize(int raw, int qp) => raw * qp;
}
