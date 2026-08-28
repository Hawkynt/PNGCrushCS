namespace SharpAstro.Jxr;

/// <summary>
/// TILE_HEADER_HIGHPASS from T.832 §8.6.4 / Table 45 — per-tile HP-band
/// header. The TRIM_FLEXBITS field is always emitted when the enclosing
/// IMAGE_HEADER set TRIM_FLEX_BITS_FLAG. The HP_QP() body is emitted only
/// when the plane-level HP_IMAGE_PLANE_UNIFORM_FLAG = 0.
/// </summary>
public sealed class TileHeaderHighpass
{
    /// <summary>TRIM_FLEXBITS — 0..15 LSBs dropped from the flexbits refinement.</summary>
    public int TrimFlexBits;

    /// <summary>When true, HP band reuses the per-tile LP QPs (NumHPQPs = NumLPQPs).</summary>
    public bool UseLpQpForHp;

    /// <summary>Per-tile HP QP table. Populated only when plane uniform = 0 AND USE_LP_QP_FLAG = 0.</summary>
    public QpTable? HpQp;

    /// <summary>Backwards-compat alias — see <see cref="TileHeaderDc.DcUniformFlag"/>.</summary>
    public bool HpUniformFlag
    {
        get => HpQp is null && !UseLpQpForHp;
        set { if (!value) throw new InvalidOperationException("Set HpQp / UseLpQpForHp to switch to per-tile QPs"); }
    }

    public void Write(BitWriter writer, bool trimFlexBitsFlag, bool planeUniform, int numComponents)
    {
        if (trimFlexBitsFlag)
        {
            if (TrimFlexBits is < 0 or > 15)
                throw new ArgumentOutOfRangeException(nameof(TrimFlexBits), TrimFlexBits, "TRIM_FLEXBITS must fit in 4 bits (0..15)");
            writer.WriteBits((uint)TrimFlexBits, 4);
        }

        if (planeUniform) return;
        writer.WriteBit(UseLpQpForHp);
        if (UseLpQpForHp) return;

        if (HpQp is null)
            throw new InvalidOperationException(
                "TileHeaderHighpass.HpQp must be populated when neither plane HP uniform nor USE_LP_QP_FLAG holds");
        if (HpQp.NumQPs is < 1 or > 16)
            throw new InvalidOperationException($"NumHPQPs must be 1..16; got {HpQp.NumQPs}");
        writer.WriteBits((uint)(HpQp.NumQPs - 1), 4);
        for (var q = 0; q < HpQp.NumQPs; q++)
        {
            var row = new byte[numComponents];
            for (var c = 0; c < numComponents; c++) row[c] = HpQp[q, c];
            QpTable.WriteOneRow(writer, numComponents, HpQp.ComponentModes[q], row);
        }
    }

    public static TileHeaderHighpass Read(ref BitReader reader, bool trimFlexBitsFlag, bool planeUniform, int numComponents)
    {
        var h = new TileHeaderHighpass();
        if (trimFlexBitsFlag)
            h.TrimFlexBits = (int)reader.ReadBits(4);
        if (planeUniform) return h;

        h.UseLpQpForHp = reader.ReadBit();
        if (h.UseLpQpForHp) return h;

        var numHpQps = (int)reader.ReadBits(4) + 1;
        var modes = new QpComponentMode[numHpQps];
        var grid = new byte[numHpQps, numComponents];
        for (var q = 0; q < numHpQps; q++)
        {
            var (mode, qps) = QpTable.ReadOneRow(ref reader, numComponents);
            modes[q] = mode;
            for (var c = 0; c < numComponents; c++) grid[q, c] = qps[c];
        }
        h.HpQp = new QpTable
        {
            NumQPs = numHpQps,
            NumComponents = numComponents,
            ComponentModes = modes,
            Qps = grid,
        };
        return h;
    }
}
