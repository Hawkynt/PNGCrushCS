namespace SharpAstro.Jxr;

/// <summary>
/// TILE_HEADER_LOWPASS from T.832 §8.6.3 / Table 43 — per-tile LP-band
/// header. Emitted only when the enclosing
/// <c>LP_IMAGE_PLANE_UNIFORM_FLAG = 0</c>; otherwise the tile header is
/// empty (the tile inherits the image-plane LP QP).
/// </summary>
/// <remarks>
/// When emitted, the structure is:
/// <code>
///   USE_DC_QP_FLAG       u(1)
///   if (!USE_DC_QP_FLAG) {
///     NUM_LP_QPS_MINUS1  u(4)
///     LP_QP()           // NumLPQPs rows, each NumComponents wide
///   }
/// </code>
/// USE_DC_QP_FLAG=1 means the LP band reuses the DC QP table from
/// <see cref="TileHeaderDc"/> (NumLPQPs = 1).
/// </remarks>
public sealed class TileHeaderLowpass
{
    /// <summary>
    /// When true, the LP band uses the per-tile DC QP table
    /// (<see cref="TileHeaderDc.DcQp"/>) instead of a dedicated LP table.
    /// Only meaningful when the plane-level LP_IMAGE_PLANE_UNIFORM_FLAG is 0.
    /// </summary>
    public bool UseDcQpForLp;

    /// <summary>
    /// Per-tile LP QP table. Populated when the plane uniform flag is 0 and
    /// USE_DC_QP_FLAG is 0. Null in all other cases.
    /// </summary>
    public QpTable? LpQp;

    /// <summary>Backwards-compat alias — see <see cref="TileHeaderDc.DcUniformFlag"/>.</summary>
    public bool LpUniformFlag
    {
        get => LpQp is null && !UseDcQpForLp;
        set { if (!value) throw new InvalidOperationException("Set LpQp / UseDcQpForLp to switch to per-tile QPs"); }
    }

    public void Write(BitWriter writer, bool planeUniform, int numComponents)
    {
        if (planeUniform) return;
        writer.WriteBit(UseDcQpForLp);
        if (UseDcQpForLp) return;

        if (LpQp is null)
            throw new InvalidOperationException(
                "TileHeaderLowpass.LpQp must be populated when neither plane LP uniform nor USE_DC_QP_FLAG holds");
        if (LpQp.NumQPs is < 1 or > 16)
            throw new InvalidOperationException($"NumLPQPs must be 1..16; got {LpQp.NumQPs}");
        writer.WriteBits((uint)(LpQp.NumQPs - 1), 4);
        for (var q = 0; q < LpQp.NumQPs; q++)
        {
            var row = new byte[numComponents];
            for (var c = 0; c < numComponents; c++) row[c] = LpQp[q, c];
            QpTable.WriteOneRow(writer, numComponents, LpQp.ComponentModes[q], row);
        }
    }

    public static TileHeaderLowpass Read(ref BitReader reader, bool planeUniform, int numComponents)
    {
        if (planeUniform) return new TileHeaderLowpass();
        var useDcQp = reader.ReadBit();
        if (useDcQp) return new TileHeaderLowpass { UseDcQpForLp = true };

        var numLpQps = (int)reader.ReadBits(4) + 1;
        var modes = new QpComponentMode[numLpQps];
        var grid = new byte[numLpQps, numComponents];
        for (var q = 0; q < numLpQps; q++)
        {
            var (mode, qps) = QpTable.ReadOneRow(ref reader, numComponents);
            modes[q] = mode;
            for (var c = 0; c < numComponents; c++) grid[q, c] = qps[c];
        }
        return new TileHeaderLowpass
        {
            LpQp = new QpTable
            {
                NumQPs = numLpQps,
                NumComponents = numComponents,
                ComponentModes = modes,
                Qps = grid,
            },
        };
    }
}
