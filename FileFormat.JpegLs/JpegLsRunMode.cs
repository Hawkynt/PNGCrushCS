using System;

namespace FileFormat.JpegLs;

/// <summary>
/// Run-length encoding mode for JPEG-LS (ITU-T T.87).
/// Handles encoding and decoding of flat regions where all quantized gradients are zero,
/// including the J-table-based segmented run coding and run interruption coding.
/// </summary>
internal static class JpegLsRunMode {

  // J table from ITU-T T.87 Table A.1 (run length order)
  private static readonly int[] _JTable = [0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 9, 10, 11, 12, 13, 14, 15];

  /// <summary>Gets the J[rk] value (log2 of segment size) for the given run index.</summary>
  internal static int GetJ(int runIndex) => runIndex < _JTable.Length ? _JTable[runIndex] : _JTable[^1];

  /// <summary>
  /// Encodes a run of identical samples starting at position (x, y) using J-table segmented coding.
  /// Returns the new x position after the run (and possible run interruption sample).
  /// </summary>
  /// <param name="writer">Bit-level output writer.</param>
  /// <param name="samples">Complete sample buffer.</param>
  /// <param name="width">Row stride.</param>
  /// <param name="x">Starting column (first sample to encode).</param>
  /// <param name="y">Row index.</param>
  /// <param name="ra">Run value (the repeated sample).</param>
  /// <param name="codec">Codec state (for run index, context stats, and parameters).</param>
  /// <returns>Updated column position after the run.</returns>
  internal static int Encode(BitWriter writer, int[] samples, int width, int x, int y, int ra, JpegLsCodec codec) {
    var rowOffset = y * width;

    // Count consecutive identical pixels
    var runStart = x;
    while (x < width && samples[rowOffset + x] == ra)
      ++x;

    var remaining = x - runStart;

    // Encode run using J table segments
    while (true) {
      var j = GetJ(codec.RunIndex);
      var segmentSize = 1 << j;

      if (remaining >= segmentSize) {
        writer.WriteBit(1);
        remaining -= segmentSize;
        if (codec.RunIndex < 31)
          ++codec.RunIndex;

        // Full segment consumed and at end of row
        if (remaining == 0 && x >= width)
          return x;
      } else {
        // Incomplete segment (includes zero-length remainder)
        writer.WriteBit(0);
        writer.WriteBits(remaining, j);
        remaining = 0;

        // Encode run interruption sample if not at end of row
        if (x < width) {
          _EncodeRunInterruption(writer, samples, width, x, y, ra, codec);
          ++x;
        }

        if (codec.RunIndex > 0)
          --codec.RunIndex;

        return x;
      }
    }
  }

  /// <summary>
  /// Decodes a run of identical samples starting at position (x, y) using J-table segmented coding.
  /// Returns the new x position after the run (and possible run interruption sample).
  /// </summary>
  /// <param name="reader">Bit-level input reader.</param>
  /// <param name="samples">Output sample buffer (partially filled).</param>
  /// <param name="width">Row stride.</param>
  /// <param name="x">Starting column.</param>
  /// <param name="y">Row index.</param>
  /// <param name="ra">Run value (the repeated sample).</param>
  /// <param name="codec">Codec state (for run index, context stats, and parameters).</param>
  /// <returns>Updated column position after the run.</returns>
  internal static int Decode(BitReader reader, int[] samples, int width, int x, int y, int ra, JpegLsCodec codec) {
    var rowOffset = y * width;

    while (true) {
      var j = GetJ(codec.RunIndex);
      var bit = reader.ReadBit();

      if (bit == 1) {
        // Complete run segment
        var segmentSize = 1 << j;
        for (var i = 0; i < segmentSize && x < width; ++i) {
          samples[rowOffset + x] = ra;
          ++x;
        }
        if (codec.RunIndex < 31)
          ++codec.RunIndex;

        if (x >= width)
          return x;
      } else {
        // Incomplete segment: read j bits for remainder count
        var remainder = reader.ReadBits(j);
        for (var i = 0; i < remainder && x < width; ++i) {
          samples[rowOffset + x] = ra;
          ++x;
        }

        // Run interruption sample
        if (x < width) {
          var rb = y > 0 ? samples[(y - 1) * width + x] : 0;
          var sample = _DecodeRunInterruption(reader, ra, rb, codec);
          samples[rowOffset + x] = sample;
          ++x;
        }

        if (codec.RunIndex > 0)
          --codec.RunIndex;

        return x;
      }
    }
  }

  /// <summary>Encodes the run interruption sample after an incomplete run segment.</summary>
  private static void _EncodeRunInterruption(BitWriter writer, int[] samples, int width, int x, int y, int ra, JpegLsCodec codec) {
    var idx = y * width + x;
    var ix = samples[idx];
    var rb = y > 0 ? samples[idx - width] : 0;

    var riType = Math.Abs(ra - rb) <= codec.Near;
    var ctx = riType ? JpegLsContext.RunContextIndex : JpegLsContext.RunInterruptContextIndex;

    var predicted = riType ? ra : rb;
    var error = ix - predicted;

    if (!riType && ra > rb)
      error = -error;

    error = JpegLsPredictor.QuantizeError(error, codec.Near);
    error = JpegLsPredictor.ReduceError(error, codec.Range);

    var k = codec.Context.ComputeK(ctx);
    var inverted = codec.Context.IsMapInverted(k, ctx);
    var mapped = JpegLsGolombCoder.MapError(error, inverted);

    JpegLsGolombCoder.Encode(writer, mapped, k, codec.Limit, codec.QBpp);

    codec.Context.Update(ctx, error);
  }

  /// <summary>Decodes the run interruption sample after an incomplete run segment.</summary>
  private static int _DecodeRunInterruption(BitReader reader, int ra, int rb, JpegLsCodec codec) {
    var riType = Math.Abs(ra - rb) <= codec.Near;
    var ctx = riType ? JpegLsContext.RunContextIndex : JpegLsContext.RunInterruptContextIndex;

    var k = codec.Context.ComputeK(ctx);
    var mapped = JpegLsGolombCoder.Decode(reader, k, codec.Limit, codec.QBpp);
    var inverted = codec.Context.IsMapInverted(k, ctx);
    var error = JpegLsGolombCoder.UnmapError(mapped, inverted);

    codec.Context.Update(ctx, error);

    var predicted = riType ? ra : rb;

    if (codec.Near > 0)
      error *= 2 * codec.Near + 1;

    if (!riType && ra > rb)
      error = -error;

    var reconstructed = predicted + error;
    if (reconstructed < 0)
      reconstructed += codec.Range;
    else if (reconstructed > codec.MaxVal)
      reconstructed -= codec.Range;

    return Math.Clamp(reconstructed, 0, codec.MaxVal);
  }
}
