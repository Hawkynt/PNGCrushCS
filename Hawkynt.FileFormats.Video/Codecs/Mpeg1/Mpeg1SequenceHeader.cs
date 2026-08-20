using System;
using System.IO;

namespace FileFormat.Codecs.Mpeg1;

/// <summary>
/// The sequence header of ISO/IEC 11172-2, 2.4.2.3: everything that is true of every picture until
/// the next one arrives.
/// </summary>
/// <remarks>
/// A stream may carry the header again before any group of pictures, which is how a decoder is able
/// to start part way into a broadcast. Repeats normally restate the same values, but they are
/// allowed to load new quantiser matrices, so this is re-read every time rather than parsed once and
/// assumed.
/// </remarks>
internal sealed class Mpeg1SequenceHeader {

  /// <summary>The picture's width in pixels, as displayed.</summary>
  internal required int Width { get; init; }

  /// <summary>The picture's height in pixels, as displayed.</summary>
  internal required int Height { get; init; }

  /// <summary>Macroblocks across, which is the width rounded up to a multiple of sixteen, over sixteen.</summary>
  internal int MacroblockWidth => (this.Width + 15) / 16;

  /// <summary>Macroblocks down.</summary>
  internal int MacroblockHeight => (this.Height + 15) / 16;

  /// <summary>The intra quantiser weighting matrix in force, in raster order.</summary>
  internal required byte[] IntraMatrix { get; init; }

  /// <summary>The non-intra quantiser weighting matrix in force, in raster order.</summary>
  internal required byte[] NonIntraMatrix { get; init; }

  /// <summary>Reads a sequence header, positioned just past its start code.</summary>
  /// <param name="reader">The bitstream.</param>
  /// <param name="previous">The header in force before this one, whose matrices carry over when this
  /// one loads neither.</param>
  internal static Mpeg1SequenceHeader Parse(ref Mpeg1BitReader reader, Mpeg1SequenceHeader? previous) {
    var width = reader.ReadBits(12);
    var height = reader.ReadBits(12);
    if (width == 0 || height == 0)
      throw new InvalidDataException(
        $"The MPEG-1 sequence header states a picture size of {width}x{height}, and neither dimension may be zero.");

    // pel_aspect_ratio and picture_rate are display geometry and display timing; neither changes a
    // sample, and there is nowhere in a RawImage to put either, so they are read to be stepped over.
    // picture_rate is still checked, because a value outside the eight the standard defines means
    // this is not a sequence header — and reading on from one that is not is how a decoder produces
    // a picture of noise instead of a refusal.
    reader.ReadBits(4);
    var pictureRate = reader.ReadBits(4);
    if (pictureRate is 0 or > 8)
      throw new InvalidDataException(
        $"The MPEG-1 sequence header states picture_rate {pictureRate}, which ISO/IEC 11172-2 Table 2-C.4 leaves "
        + "forbidden or reserved. Rates 1 to 8 are the ones the standard defines.");

    reader.ReadBits(18); // bit_rate
    if (reader.ReadBit() != 1)
      throw new InvalidDataException(
        "The marker bit in the MPEG-1 sequence header is zero, so this is not a sequence header or the stream is corrupt.");

    reader.ReadBits(10); // vbv_buffer_size
    reader.ReadBit();    // constrained_parameters_flag

    // The matrices are transmitted in the zig-zag scan order the coefficients are, and are held here
    // in raster order so that dequantisation indexes them the same way it indexes the block.
    var intra = reader.ReadBit() == 1
      ? _ReadMatrix(ref reader)
      : previous?.IntraMatrix ?? Mpeg1Quantisation.DefaultIntraMatrix;

    var nonIntra = reader.ReadBit() == 1
      ? _ReadMatrix(ref reader)
      : previous?.NonIntraMatrix ?? Mpeg1Quantisation.DefaultNonIntraMatrix;

    return new() {
      Width = width,
      Height = height,
      IntraMatrix = intra,
      NonIntraMatrix = nonIntra,
    };
  }

  /// <summary>
  /// Reads a loaded quantiser matrix and un-zig-zags it.
  /// </summary>
  /// <remarks>
  /// A weight of zero is refused rather than used: it would make every coefficient it applies to
  /// reconstruct as zero regardless of what was coded, which is not a quantiser but a way of
  /// discarding the picture. 11172-2 2.4.2.3 forbids it outright.
  /// </remarks>
  private static byte[] _ReadMatrix(ref Mpeg1BitReader reader) {
    var matrix = new byte[64];
    for (var scan = 0; scan < 64; ++scan) {
      var weight = reader.ReadBits(8);
      if (weight == 0)
        throw new InvalidDataException(
          $"The quantiser matrix loaded by the MPEG-1 sequence header holds a zero at scan position {scan}, which the standard forbids.");

      matrix[Mpeg1Quantisation.ZigZag[scan]] = (byte)weight;
    }

    return matrix;
  }

  /// <summary>Whether another sequence header describes the same picture geometry as this one.</summary>
  internal bool SameGeometryAs(Mpeg1SequenceHeader other) {
    ArgumentNullException.ThrowIfNull(other);

    return this.Width == other.Width && this.Height == other.Height;
  }
}
