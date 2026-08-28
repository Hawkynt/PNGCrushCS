namespace SharpAstro.Jxr;

/// <summary>
/// IMAGE_PLANE_HEADER from the JXR codestream — T.832 §8.4. Each image
/// plane (luma plane and optional alpha plane) is prefixed by one of these
/// structures, which selects internal colour format, declares which subbands
/// (DC / LP / HP / FlexBits) are present, and carries the uniform-quantization
/// step sizes for each present band.
/// </summary>
/// <remarks>
/// This first cut targets the common single-tile spatial-mode encoder path
/// and intentionally restricts:
/// <list type="bullet">
///   <item>Non-uniform per-MB quantization (the IMAGE_PLANE_UNIFORM_FLAG=false
///         path lands with the tile-header work). Only uniform-quant is supported.</item>
///   <item>YUV chroma centering — INTERNAL_CLR_FMT must currently be one of
///         YOnly / YUV444 / YUVK / NComponent / Rgb / Rgbe. YUV420/422 add
///         CHROMA_CENTERING_{X,Y} fields and are deferred.</item>
///   <item>NComponent extension (NUM_COMPONENTS_MINUS1 == 0xF). NumComponents
///         is therefore restricted to 1..16.</item>
/// </list>
/// </remarks>
public sealed class ImagePlaneHeader
{
    public JxrInternalColorFormat InternalClrFmt;
    public bool ScaledFlag;                 // T.832 8.4.3 — pre-scaling stage active
    public JxrBandsPresent BandsPresent;

    /// <summary>NUM_COMPONENTS_MINUS1 + 1 — only meaningful when INTERNAL_CLR_FMT == NComponent.</summary>
    public int NumComponents = 1;

    /// <summary>SHIFT_BITS — present only for OUTPUT_BITDEPTH in {Bd16, Bd16S, Bd32S}.</summary>
    public byte ShiftBits;

    /// <summary>LEN_MANTISSA — present only for OUTPUT_BITDEPTH == Bd32F.</summary>
    public byte LenMantissa;

    /// <summary>EXP_BIAS — present only for OUTPUT_BITDEPTH == Bd32F (signed 8-bit).</summary>
    public sbyte ExpBias;

    /// <summary>DC band uniform-quantization step. Always present.</summary>
    public byte DcQuant;

    /// <summary>LP band uniform-quantization step (only meaningful when LP band is present and not aliased to DC).</summary>
    public byte LpQuant;

    /// <summary>True when LP band reuses DC quantization (saves bits in the codestream).</summary>
    public bool UseDcQpForLp;

    /// <summary>HP band uniform-quantization step (only meaningful when HP band is present and not aliased to LP).</summary>
    public byte HpQuant;

    /// <summary>True when HP band reuses LP quantization.</summary>
    public bool UseLpQpForHp;

    /// <summary>
    /// DC_IMAGE_PLANE_UNIFORM_FLAG (T.832 §8.4.16). When true (default),
    /// <see cref="DcQuant"/> applies image-wide. When false, the DC QP table
    /// is emitted per-tile inside each <see cref="TileHeaderDc"/>.
    /// </summary>
    public bool DcImagePlaneUniformFlag = true;

    /// <summary>LP_IMAGE_PLANE_UNIFORM_FLAG (T.832 §8.4.18). Default true.</summary>
    public bool LpImagePlaneUniformFlag = true;

    /// <summary>HP_IMAGE_PLANE_UNIFORM_FLAG (T.832 §8.4.20). Default true.</summary>
    public bool HpImagePlaneUniformFlag = true;

    /// <summary>
    /// Plane-level DC QP vector. Populated when <see cref="DcImagePlaneUniformFlag"/> is true
    /// AND the plane is multi-component. <c>null</c> in single-component or non-uniform-at-plane
    /// configurations — the simpler <see cref="DcQuant"/> still mirrors the luma value for
    /// backwards compat in the single-component case.
    /// </summary>
    public QpTable? PlaneDcQp;

    /// <summary>Plane-level LP QP vector. See <see cref="PlaneDcQp"/>.</summary>
    public QpTable? PlaneLpQp;

    /// <summary>Plane-level HP QP vector. See <see cref="PlaneDcQp"/>.</summary>
    public QpTable? PlaneHpQp;

    /// <summary>
    /// Write this header to <paramref name="writer"/>. Caller supplies the
    /// <paramref name="outputBitDepth"/> from the enclosing IMAGE_HEADER —
    /// the bitstream layout of SHIFT_BITS / LEN_MANTISSA / EXP_BIAS depends on it.
    /// </summary>
    public void Write(BitWriter writer, JxrOutputBitDepth outputBitDepth)
    {
        if (InternalClrFmt == JxrInternalColorFormat.YUV420 || InternalClrFmt == JxrInternalColorFormat.YUV422)
            throw new NotSupportedException("YUV420/YUV422 internal formats not yet supported in IMAGE_PLANE_HEADER.Write");

        writer.WriteBits((uint)InternalClrFmt, 3);
        writer.WriteBit(ScaledFlag);
        writer.WriteBits((uint)BandsPresent, 4);

        // T.832 Table 27: the YUV / NComponent chroma block is always 8 bits.
        // YUV444: RESERVED_F u(4) + RESERVED_H u(4).
        // NComponent (non-extended): NUM_COMPONENTS_MINUS1 u(4) + RESERVED_H u(4).
        // CHROMA_CENTERING_{X,Y} blocks for YUV420/422 elided (rejected above).
        if (InternalClrFmt == JxrInternalColorFormat.YUV444)
        {
            writer.WriteBits(0, 4); // RESERVED_F
            writer.WriteBits(0, 4); // RESERVED_H
        }

        if (InternalClrFmt == JxrInternalColorFormat.NComponent)
        {
            if (NumComponents is < 1 or > 16)
                throw new NotSupportedException($"NComponent NumComponents must be 1..16 (extension path not yet supported), got {NumComponents}");
            writer.WriteBits((uint)(NumComponents - 1), 4);
            writer.WriteBits(0, 4); // RESERVED_H (non-extended path)
        }

        switch (outputBitDepth)
        {
            case JxrOutputBitDepth.Bd16:
            case JxrOutputBitDepth.Bd16S:
            case JxrOutputBitDepth.Bd32S:
                writer.WriteBits(ShiftBits, 8);
                break;
            case JxrOutputBitDepth.Bd32F:
                writer.WriteBits(LenMantissa, 8);
                writer.WriteBits((uint)(byte)ExpBias, 8); // EXP_BIAS = i(8); raw 8 bits, two's-complement.
                break;
            // Bd16F: nothing — per T.832 §8.4 / Table 28 and jxrlib's reference
            // encoder, the half-float plane header has no LEN_MANTISSA / EXP_BIAS
            // fields. Emitting them mis-aligned the rest of the codestream by 16
            // bits and made WIC's WMPhotoDecoder reject the file with FRAMES=0
            // (the small-image path tolerated the misalignment well enough to
            // instantiate a frame with FMT=Default but full pixel decode still
            // broke). Task #11 in the WIC oracle harness.
        }

        // For named formats NumComponents is implied by InternalClrFmt — caller
        // doesn't need to set it. We mirror Read here so the round-trip works
        // even if the caller leaves NumComponents at its default.
        var effectiveComponents = InternalClrFmt == JxrInternalColorFormat.NComponent
            ? NumComponents
            : ComponentCountFor(InternalClrFmt);

        // DC band.
        writer.WriteBit(DcImagePlaneUniformFlag);
        if (DcImagePlaneUniformFlag)
            WritePlaneQpRow(writer, effectiveComponents, DcQuant);

        // T.832 §8.4: plane-level header for LP is RESERVED_I_BIT + LP_IMAGE_PLANE_UNIFORM_FLAG.
        // USE_DC_QP_FLAG lives at TILE level (TileHeaderLowpass), NOT here.
        if (BandsPresent != JxrBandsPresent.DcOnly)
        {
            writer.WriteBit(false);        // RESERVED_I_BIT
            writer.WriteBit(LpImagePlaneUniformFlag);
            if (LpImagePlaneUniformFlag)
                WritePlaneQpRow(writer, effectiveComponents, LpQuant);
        }

        if (BandsPresent != JxrBandsPresent.DcOnly && BandsPresent != JxrBandsPresent.NoHighpass)
        {
            writer.WriteBit(false);        // RESERVED_J_BIT
            writer.WriteBit(HpImagePlaneUniformFlag);
            if (HpImagePlaneUniformFlag)
                WritePlaneQpRow(writer, effectiveComponents, HpQuant);
        }

        // byte_alignment() — pad to byte boundary (T.832 5.3.3).
        WriteByteAlignment(writer);
    }

    /// <summary>Read an IMAGE_PLANE_HEADER from <paramref name="reader"/>.</summary>
    public static ImagePlaneHeader Read(ref BitReader reader, JxrOutputBitDepth outputBitDepth)
    {
        var h = new ImagePlaneHeader
        {
            InternalClrFmt = (JxrInternalColorFormat)reader.ReadBits(3),
            ScaledFlag = reader.ReadBit(),
            BandsPresent = (JxrBandsPresent)reader.ReadBits(4),
        };

        if (h.InternalClrFmt == JxrInternalColorFormat.YUV420 || h.InternalClrFmt == JxrInternalColorFormat.YUV422)
            throw new NotSupportedException($"YUV420/YUV422 internal formats not yet supported (CHROMA_CENTERING_{{X,Y}} parsing pending), got {h.InternalClrFmt}");
        if (h.InternalClrFmt == JxrInternalColorFormat.YUV444)
        {
            reader.SkipBits(4); // RESERVED_F
            reader.SkipBits(4); // RESERVED_H
        }

        // NumComponents is bitstream-encoded only for NComponent; for the named
        // formats it's implied by InternalClrFmt — derive it so downstream code
        // (e.g. MbDc.DecodeMb's VAL_DC_YUV path) sees the correct component count.
        h.NumComponents = ComponentCountFor(h.InternalClrFmt);

        if (h.InternalClrFmt == JxrInternalColorFormat.NComponent)
        {
            var numMinus1 = (int)reader.ReadBits(4);
            if (numMinus1 == 0xF)
                throw new NotSupportedException("NComponent extension (>=17 components) not yet supported");
            h.NumComponents = numMinus1 + 1;
            reader.SkipBits(4); // RESERVED_H (non-extended path)
        }

        switch (outputBitDepth)
        {
            case JxrOutputBitDepth.Bd16:
            case JxrOutputBitDepth.Bd16S:
            case JxrOutputBitDepth.Bd32S:
                h.ShiftBits = (byte)reader.ReadBits(8);
                break;
            case JxrOutputBitDepth.Bd32F:
                h.LenMantissa = (byte)reader.ReadBits(8);
                h.ExpBias = unchecked((sbyte)reader.ReadBits(8));
                break;
            // Bd16F: nothing — see matching comment in Write().
        }

        // DC band. When DC_IMAGE_PLANE_UNIFORM_FLAG = 0, the DC_QP() block is
        // emitted per-tile in TILE_HEADER_DC, not here.
        h.DcImagePlaneUniformFlag = reader.ReadBit();
        if (h.DcImagePlaneUniformFlag)
        {
            var (mode, qps) = QpTable.ReadOneRow(ref reader, h.NumComponents);
            h.PlaneDcQp = BuildQpTable(mode, qps, h.NumComponents);
            h.DcQuant = qps[0];
        }

        if (h.BandsPresent != JxrBandsPresent.DcOnly)
        {
            reader.ReadBit(); // RESERVED_I_BIT — decoder ignores
            h.LpImagePlaneUniformFlag = reader.ReadBit();
            if (h.LpImagePlaneUniformFlag)
            {
                var (mode, qps) = QpTable.ReadOneRow(ref reader, h.NumComponents);
                h.PlaneLpQp = BuildQpTable(mode, qps, h.NumComponents);
                h.LpQuant = qps[0];
            }
            // else: LP_QP() emitted per-tile.
        }

        if (h.BandsPresent != JxrBandsPresent.DcOnly && h.BandsPresent != JxrBandsPresent.NoHighpass)
        {
            reader.ReadBit(); // RESERVED_J_BIT
            h.HpImagePlaneUniformFlag = reader.ReadBit();
            if (h.HpImagePlaneUniformFlag)
            {
                var (mode, qps) = QpTable.ReadOneRow(ref reader, h.NumComponents);
                h.PlaneHpQp = BuildQpTable(mode, qps, h.NumComponents);
                h.HpQuant = qps[0];
            }
            // else: HP_QP() emitted per-tile.
        }

        AlignToByte(ref reader);
        return h;
    }

    /// <summary>
    /// Write one plane-level QP row at the encoder's convention (uniform mode
    /// when multi-component — we don't currently emit Separate / Independent
    /// at the plane level since our encoder always wants one QP for the whole
    /// image regardless of component).
    /// </summary>
    private static void WritePlaneQpRow(BitWriter writer, int numComponents, byte qp)
    {
        var row = new byte[numComponents];
        for (var c = 0; c < numComponents; c++) row[c] = qp;
        QpTable.WriteOneRow(writer, numComponents, QpComponentMode.Uniform, row);
    }

    private static QpTable BuildQpTable(QpComponentMode mode, byte[] perComp, int numComponents)
    {
        var grid = new byte[1, numComponents];
        for (var c = 0; c < numComponents; c++) grid[0, c] = perComp[c];
        return new QpTable
        {
            NumQPs = 1,
            NumComponents = numComponents,
            ComponentModes = [mode],
            Qps = grid,
        };
    }

    private static void WriteByteAlignment(BitWriter writer)
    {
        // Pad with zero bits until on a byte boundary. WriteBit increments the
        // byte cursor when the eighth bit lands, so just pad until BitPosition
        // is a multiple of 8.
        while ((writer.BitPosition & 7) != 0) writer.WriteBit(false);
    }

    private static void AlignToByte(ref BitReader reader)
    {
        var slack = (8 - (reader.BitPosition & 7)) & 7;
        if (slack > 0) reader.SkipBits(slack);
    }

    /// <summary>
    /// Component count implied by <paramref name="fmt"/>. For NComponent the
    /// real count lives in the bitstream and overrides this default.
    /// </summary>
    public static int ComponentCountFor(JxrInternalColorFormat fmt) => fmt switch
    {
        JxrInternalColorFormat.YOnly      => 1,
        JxrInternalColorFormat.YUV420     => 3,
        JxrInternalColorFormat.YUV422     => 3,
        JxrInternalColorFormat.YUV444     => 3,
        JxrInternalColorFormat.YUVK       => 4,
        JxrInternalColorFormat.Rgb        => 3,
        JxrInternalColorFormat.Rgbe       => 4,
        JxrInternalColorFormat.NComponent => 1, // placeholder; real value comes from bitstream
        _ => throw new ArgumentOutOfRangeException(nameof(fmt), fmt, "unknown internal colour format"),
    };
}
