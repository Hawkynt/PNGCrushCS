namespace SharpAstro.Jxr;

/// <summary>
/// Composite of the three per-tile band headers (T.832 §8.6.1). Each
/// per-band header is empty when the enclosing IMAGE_PLANE_HEADER set the
/// matching plane-level uniform flag to true; when false the band header
/// carries a per-tile QP table (<see cref="QpTable"/>).
/// </summary>
public sealed class TileBandHeaders
{
    public TileHeaderDc Dc { get; init; } = new();
    public TileHeaderLowpass? Lowpass { get; init; }
    public TileHeaderHighpass? Highpass { get; init; }

    /// <summary>
    /// Build a default trio for the given bands. All sub-headers default to
    /// the plane-uniform path (empty contents) since the legacy encoder
    /// emitted only uniform-QP files; callers that switch to non-uniform
    /// must populate <see cref="TileHeaderDc.DcQp"/>, etc.
    /// </summary>
    public static TileBandHeaders Uniform(JxrBandsPresent bands) => new()
    {
        Dc = new TileHeaderDc(),
        Lowpass = bands != JxrBandsPresent.DcOnly ? new TileHeaderLowpass() : null,
        Highpass = (bands != JxrBandsPresent.DcOnly && bands != JxrBandsPresent.NoHighpass)
            ? new TileHeaderHighpass() : null,
    };

    /// <summary>
    /// Write all per-band headers. Plane-level uniform flags + the component
    /// count come from the enclosing <see cref="ImagePlaneHeader"/>.
    /// </summary>
    public void Write(BitWriter writer, JxrBandsPresent bands, bool trimFlexBitsFlag,
        ImagePlaneHeader plane)
    {
        Dc.Write(writer, plane.DcImagePlaneUniformFlag, plane.NumComponents);
        if (bands != JxrBandsPresent.DcOnly)
        {
            if (Lowpass is null)
                throw new InvalidOperationException($"Lowpass header required when BandsPresent={bands}");
            // When the LP band reuses DC QPs at the plane level
            // (UseDcQpForLp=true on the plane), the LP plane uniform flag
            // doesn't get emitted at all — so the tile-header LP is empty.
            // Otherwise the LP plane uniform flag dictates tile-header content.
            var lpPlaneUniform = plane.LpImagePlaneUniformFlag;
            Lowpass.Write(writer, lpPlaneUniform, plane.NumComponents);
        }
        if (bands != JxrBandsPresent.DcOnly && bands != JxrBandsPresent.NoHighpass)
        {
            if (Highpass is null)
                throw new InvalidOperationException($"Highpass header required when BandsPresent={bands}");
            var hpPlaneUniform = plane.HpImagePlaneUniformFlag;
            Highpass.Write(writer, trimFlexBitsFlag, hpPlaneUniform, plane.NumComponents);
        }
    }

    public static TileBandHeaders Read(ref BitReader reader, JxrBandsPresent bands, bool trimFlexBitsFlag,
        ImagePlaneHeader plane)
    {
        var dc = TileHeaderDc.Read(ref reader, plane.DcImagePlaneUniformFlag, plane.NumComponents);
        TileHeaderLowpass? lp = null;
        TileHeaderHighpass? hp = null;
        if (bands != JxrBandsPresent.DcOnly)
        {
            var lpPlaneUniform = plane.LpImagePlaneUniformFlag;
            lp = TileHeaderLowpass.Read(ref reader, lpPlaneUniform, plane.NumComponents);
        }
        if (bands != JxrBandsPresent.DcOnly && bands != JxrBandsPresent.NoHighpass)
        {
            var hpPlaneUniform = plane.HpImagePlaneUniformFlag;
            hp = TileHeaderHighpass.Read(ref reader, trimFlexBitsFlag, hpPlaneUniform, plane.NumComponents);
        }
        return new TileBandHeaders { Dc = dc, Lowpass = lp, Highpass = hp };
    }
}
