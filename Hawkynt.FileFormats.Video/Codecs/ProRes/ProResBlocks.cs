using System;

namespace FileFormat.Codecs.ProRes;

/// <summary>
/// One 8×8 block, from its place in a slice's scanned coefficient array to samples in a plane.
/// </summary>
/// <remarks>
/// The four steps RDD 36:2022, 7 lists after entropy decoding, done together because nothing else
/// wants any of the intermediates: inverse scanning (7.2), inverse quantisation (7.3), the inverse
/// transform (7.4) and sample generation (7.5.1).
/// <para/>
/// <b>Inverse slice scanning is an index calculation, not a table.</b> 7.2.1 gives
/// <c>QFS(m,b)[n] = scannedCoeffs[nB * sliceSizeInMb * n + nB * m + b]</c>: every block of the slice
/// contributes its frequency <c>n</c> coefficient before any block contributes its frequency
/// <c>n+1</c> one. That interleaving is why a slice is coded as one stream rather than as a
/// sequence of blocks — the runs of zeroes at high frequency then span the whole slice instead of
/// restarting at every block.
/// </remarks>
internal static class ProResBlocks {

  /// <summary>
  /// Reconstructs one block and writes its samples into a plane.
  /// </summary>
  /// <param name="scanned">The whole component's scanned coefficient array for this slice.</param>
  /// <param name="blocksPerMacroblock">Blocks of this component in one macroblock: 4 for luma,
  /// 2 or 4 for chroma.</param>
  /// <param name="sliceSizeInMacroblocks">The width of this slice in macroblocks.</param>
  /// <param name="macroblock">The block's macroblock index within the slice.</param>
  /// <param name="block">The block's index within its macroblock.</param>
  /// <param name="weights">The quantisation weight matrix for this component, raster order.</param>
  /// <param name="qScale">The slice's quantisation scale factor.</param>
  /// <param name="scan">The block scan pattern the picture's interlace mode selects.</param>
  /// <param name="target">The plane the samples are written to.</param>
  /// <param name="planeWidth">The width of that plane in samples.</param>
  /// <param name="planeHeight">The height of that plane in samples; rows past it are discarded.</param>
  /// <param name="originX">The block's left column in the plane.</param>
  /// <param name="originY">The block's top row within the <i>picture</i>, before field mapping.</param>
  /// <param name="fieldOffset">The plane row picture row 0 maps to: 0 for a frame picture or the
  /// top field, 1 for the bottom field.</param>
  /// <param name="fieldStep">1 for a frame picture, 2 for a field picture.</param>
  /// <param name="bitDepth">The sample depth <c>b</c> of RDD 36:2022, 7.5.1.</param>
  internal static void Reconstruct(
    int[] scanned,
    int blocksPerMacroblock,
    int sliceSizeInMacroblocks,
    int macroblock,
    int block,
    ReadOnlySpan<byte> weights,
    int qScale,
    int[] scan,
    ushort[] target,
    int planeWidth,
    int planeHeight,
    int originX,
    int originY,
    int fieldOffset,
    int fieldStep,
    int bitDepth) {
    Span<double> coefficients = stackalloc double[64];

    var stride = blocksPerMacroblock * sliceSizeInMacroblocks;
    var offset = blocksPerMacroblock * macroblock + block;

    // Inverse scanning and inverse quantisation in one pass. 7.3: F[v][u] = QF[v][u] * W[v][u] *
    // qScale / 8, and the eighth is kept rather than rounded away — the coefficients are always
    // multiples of an eighth and 7.3 requires at least quarter-integer precision to survive into the
    // transform.
    for (var i = 0; i < 64; ++i) {
      var quantised = scanned[stride * scan[i] + offset];
      coefficients[i] = quantised == 0 ? 0d : quantised * weights[i] * qScale / 8d;
    }

    ProResInverseDct.Transform(coefficients);

    // 7.5.1: s = clamp(round(2^b * (v + 256) / 512)). The transform's output runs from −256 up to
    // just under 256, so the addition restores mid-range bias and the division scales that span onto
    // the sample depth.
    var scale = (1 << bitDepth) / 512d;
    var lowest = LowestSample(bitDepth);
    var highest = HighestSample(bitDepth);

    for (var y = 0; y < 8; ++y) {
      var row = fieldOffset + (originY + y) * fieldStep;
      if (row >= planeHeight)
        break;

      var into = row * planeWidth + originX;
      for (var x = 0; x < 8; ++x) {
        var sample = (int)Math.Round(scale * (coefficients[y * 8 + x] + 256d), MidpointRounding.AwayFromZero);
        target[into + x] = (ushort)(sample < lowest ? lowest : sample > highest ? highest : sample);
      }
    }
  }

  /// <summary>
  /// The smallest sample value a component may take, RDD 36:2022, 7.5.1.
  /// </summary>
  /// <remarks>
  /// 7.5.1 offers a decoder two sets of clamping bounds: 0 and <c>2^b − 1</c>, which use every
  /// quantisation level, or the smallest and largest <i>permissible video</i> levels, which keep
  /// clear of the quantisation levels ITU-R BT.601 and BT.709 reserve for synchronisation and timing
  /// references. This decoder takes the second, which at ten bits is 4 to 1019 and at twelve is 16
  /// to 4079 — the reserved levels being the lowest and highest <c>2^(b−8)</c> of the range in each
  /// case.
  /// <para/>
  /// The narrower bounds are the right ones for a decoder whose output is a video signal, and they
  /// cost nothing downstream: the studio-swing expansion in <see cref="ProResColorConversion"/> maps
  /// everything below the black level to zero anyway, so the samples the two choices disagree about
  /// are ones that convert to the same colour either way. They are also what ffmpeg produces, which
  /// is why choosing the wider bounds shows up in a plane comparison as a scatter of samples exactly
  /// four levels apart at the extremes of a heavily quantised picture — 2135 of them across ten
  /// frames of ProRes 422 Proxy — and nowhere else.
  /// </remarks>
  internal static int LowestSample(int bitDepth) => 1 << (bitDepth - 8);

  /// <summary>The largest sample value a component may take, RDD 36:2022, 7.5.1.</summary>
  internal static int HighestSample(int bitDepth) => (1 << bitDepth) - 1 - (1 << (bitDepth - 8));
}
