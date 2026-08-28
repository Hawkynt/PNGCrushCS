namespace SharpAstro.Jxr;

/// <summary>
/// TILE_HEADER_DC from T.832 §8.6.2 / Table 41. The header is empty when
/// <c>DC_IMAGE_PLANE_UNIFORM_FLAG = 1</c> (the tile inherits the
/// image-plane DC quantization step); when the plane uniform flag is 0
/// the tile header carries a single DC_QP() row with per-component QPs.
/// </summary>
public sealed class TileHeaderDc
{
    /// <summary>
    /// Per-tile DC quantization table. <c>null</c> when the enclosing
    /// IMAGE_PLANE_HEADER has DC_IMAGE_PLANE_UNIFORM_FLAG = 1, in which
    /// case all tiles share the same <c>ImagePlaneHeader.DcQuant</c> and
    /// the tile-header itself emits zero bits.
    /// </summary>
    public QpTable? DcQp;

    /// <summary>
    /// Backwards-compat alias. True when the tile inherits the image-plane
    /// DC QP (no per-tile table), false when this tile carries its own
    /// <see cref="DcQp"/>. Setter accepted but ignored — set <see cref="DcQp"/>
    /// directly to switch to per-tile QPs.
    /// </summary>
    public bool DcUniformFlag
    {
        get => DcQp is null;
        set { if (!value) throw new InvalidOperationException("Set DcQp to switch to per-tile QPs"); }
    }

    /// <summary>Write this tile-header. The <paramref name="planeUniform"/> bit comes from the enclosing ImagePlaneHeader.</summary>
    public void Write(BitWriter writer, bool planeUniform, int numComponents)
    {
        if (planeUniform)
            return; // Tile-header is empty per T.832 Table 41.
        if (DcQp is null)
            throw new InvalidOperationException(
                "TileHeaderDc.DcQp must be populated when ImagePlaneHeader.DcImagePlaneUniformFlag is false");
        // DC_QP() — one row, NumQPs is implicitly 1 for DC.
        if (DcQp.NumQPs != 1)
            throw new InvalidOperationException($"DC_QP must have exactly one row; got {DcQp.NumQPs}");
        var row = new byte[numComponents];
        for (var c = 0; c < numComponents; c++) row[c] = DcQp[0, c];
        QpTable.WriteOneRow(writer, numComponents, DcQp.ComponentModes[0], row);
    }

    public static TileHeaderDc Read(ref BitReader reader, bool planeUniform, int numComponents)
    {
        if (planeUniform) return new TileHeaderDc();
        var (mode, qps) = QpTable.ReadOneRow(ref reader, numComponents);
        var grid = new byte[1, numComponents];
        for (var c = 0; c < numComponents; c++) grid[0, c] = qps[c];
        return new TileHeaderDc
        {
            DcQp = new QpTable
            {
                NumQPs = 1,
                NumComponents = numComponents,
                ComponentModes = [mode],
                Qps = grid,
            },
        };
    }
}
