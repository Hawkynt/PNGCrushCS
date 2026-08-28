namespace SharpAstro.Jxr;

/// <summary>
/// A DC_QP() / LP_QP() / HP_QP() block — T.832 §8.4.22, §8.4.23, §8.4.24.
/// Carries one or more per-component QP vectors. The number of vectors is
/// per-band (1 for DC, NumLPQPs / NumHPQPs for LP / HP). The shape inside
/// each vector depends on <see cref="ComponentMode"/>: UNIFORM = single
/// shared QP across all components, SEPARATE = luma vs chroma split,
/// INDEPENDENT = per-component values.
/// </summary>
/// <remarks>
/// <para>For a single-component image the COMPONENT_MODE bits are absent in
/// the bitstream and the single QP applies directly.</para>
/// <para>Encoder/decoder both materialize the table as an
/// <c>int[numQPs, numComponents]</c> grid so per-MB dequant just indexes
/// <c>(qpIndex, component)</c>.</para>
/// </remarks>
public sealed class QpTable
{
    /// <summary>Number of QP rows in the table — 1 for DC; NumLPQPs / NumHPQPs for LP / HP.</summary>
    public required int NumQPs { get; init; }

    /// <summary>Number of components in each row (== ImagePlaneHeader.NumComponents).</summary>
    public required int NumComponents { get; init; }

    /// <summary>Layout chosen by the encoder for each QP row.</summary>
    public required QpComponentMode[] ComponentModes { get; init; }

    /// <summary>QPs[<paramref name="row"/>, <paramref name="component"/>]. Always materialized to per-component values regardless of COMPONENT_MODE.</summary>
    public required byte[,] Qps { get; init; }

    /// <summary>QP for a given row + component — convenience indexer for dequant code.</summary>
    public byte this[int qpRow, int component] => Qps[qpRow, component];

    /// <summary>Build a single-row, all-uniform table (the simplest case — what we used to emit pre-Phase-20).</summary>
    public static QpTable Uniform(int numComponents, byte qp) => new()
    {
        NumQPs = 1,
        NumComponents = numComponents,
        ComponentModes = [QpComponentMode.Uniform],
        Qps = FillRow(numComponents, qp),
    };

    private static byte[,] FillRow(int numComponents, byte qp)
    {
        var q = new byte[1, numComponents];
        for (var c = 0; c < numComponents; c++) q[0, c] = qp;
        return q;
    }

    /// <summary>
    /// Read one DC_QP / LP_QP / HP_QP row in the encoder/decoder bitstream form.
    /// Returns the per-component vector for that row.
    /// </summary>
    internal static (QpComponentMode mode, byte[] perComponentQp) ReadOneRow(
        ref BitReader reader, int numComponents)
    {
        QpComponentMode mode;
        var qps = new byte[numComponents];
        if (numComponents == 1)
        {
            mode = QpComponentMode.Uniform;
            qps[0] = (byte)reader.ReadBits(8);
            return (mode, qps);
        }

        mode = (QpComponentMode)reader.ReadBits(2);
        switch (mode)
        {
            case QpComponentMode.Uniform:
                var u = (byte)reader.ReadBits(8);
                for (var c = 0; c < numComponents; c++) qps[c] = u;
                break;
            case QpComponentMode.Separate:
                qps[0] = (byte)reader.ReadBits(8);              // luma
                var chroma = (byte)reader.ReadBits(8);
                for (var c = 1; c < numComponents; c++) qps[c] = chroma;
                break;
            case QpComponentMode.Independent:
                for (var c = 0; c < numComponents; c++)
                    qps[c] = (byte)reader.ReadBits(8);
                break;
            default:
                throw new InvalidDataException(
                    $"QP COMPONENT_MODE = {(int)mode} is reserved (T.832 §8.4.22)");
        }
        return (mode, qps);
    }

    /// <summary>Write one row.</summary>
    internal static void WriteOneRow(
        BitWriter writer, int numComponents, QpComponentMode mode, byte[] perComponentQp)
    {
        if (numComponents == 1)
        {
            writer.WriteBits(perComponentQp[0], 8);
            return;
        }
        writer.WriteBits((uint)mode, 2);
        switch (mode)
        {
            case QpComponentMode.Uniform:
                writer.WriteBits(perComponentQp[0], 8);
                break;
            case QpComponentMode.Separate:
                writer.WriteBits(perComponentQp[0], 8); // luma
                // Spec says chroma value applies to all components 1..N-1 — caller
                // must have ensured they're identical; we just emit perComponentQp[1].
                writer.WriteBits(perComponentQp[1], 8);
                break;
            case QpComponentMode.Independent:
                for (var c = 0; c < numComponents; c++)
                    writer.WriteBits(perComponentQp[c], 8);
                break;
        }
    }
}

/// <summary>COMPONENT_MODE in DC_QP / LP_QP / HP_QP — T.832 §8.4.22 Table 32.</summary>
public enum QpComponentMode
{
    /// <summary>Single QP shared across every component.</summary>
    Uniform = 0,
    /// <summary>Luma QP + Chroma QP, where chroma applies to all non-luma components.</summary>
    Separate = 1,
    /// <summary>One QP per component, written verbatim.</summary>
    Independent = 2,
}
