namespace FileFormat.Codecs.Theora;

/// <summary>
/// The in-loop deblocking filter that finishes a reconstructed frame.
/// </summary>
/// <remarks>
/// Theora specification section 7.10. A four-tap edge detector is run across every coded block edge
/// and the two samples either side of it are moved towards each other by a tapered response — enough
/// to soften a blocking artefact, and tapering back to nothing where the step is large enough to be
/// a real edge in the picture rather than an artefact of the transform.
/// <para/>
/// It is in the loop, so it happens before the frame becomes a reference and every later frame
/// predicts from the filtered samples. That makes the order the edges are filtered in part of the
/// format rather than an implementation detail: each filter application changes samples the next one
/// reads, so filtering a block's left edge before its bottom edge gives different numbers from the
/// other way round. The order below is the specification's, which is VP3's.
/// <para/>
/// The rule about which edges get filtered is the one that is easy to get subtly wrong. A block's
/// left and bottom edges are filtered whenever the block is coded and is not against the frame
/// boundary; its right and top edges are filtered only when the neighbour on that side is *not*
/// coded — because if it were, that neighbour's own left or bottom edge would filter the same edge
/// when its turn came, and filtering it twice is not the same as filtering it once.
/// </remarks>
internal static class TheoraLoopFilter {

  /// <summary>
  /// The tapered response applied to an edge detector reading — section 7.10.
  /// </summary>
  /// <remarks>
  /// The identity between −L and L, falling linearly back to zero at ±2L, and zero beyond. A small
  /// step across a block edge is smoothed away entirely; a large one is left alone, because a large
  /// step is a picture rather than an artefact.
  /// </remarks>
  private static int _Limit(int response, int limit) {
    if (response <= -2 * limit || response >= 2 * limit)
      return 0;

    if (response > -limit && response < limit)
      return response;

    return response < 0 ? -response - 2 * limit : -response + 2 * limit;
  }

  /// <summary>Filters a vertical block edge, across each of eight rows — section 7.10.1.</summary>
  /// <remarks>
  /// Four samples wide, centred on the edge: the two outer ones are read and the two inner ones are
  /// moved. Called "horizontal" in the specification because the filter runs horizontally; the edge
  /// it works on is vertical.
  /// </remarks>
  private static void _FilterHorizontally(byte[] plane, int width, int filterX, int filterY, int limit) {
    for (var row = 0; row < 8; ++row) {
      var at = (filterY + row) * width + filterX;
      var response = (plane[at] - 3 * plane[at + 1] + 3 * plane[at + 2] - plane[at + 3] + 4) >> 3;
      var adjustment = _Limit(response, limit);

      plane[at + 1] = _Clamp(plane[at + 1] + adjustment);
      plane[at + 2] = _Clamp(plane[at + 2] - adjustment);
    }
  }

  /// <summary>Filters a horizontal block edge, down each of eight columns — section 7.10.2.</summary>
  private static void _FilterVertically(byte[] plane, int width, int filterX, int filterY, int limit) {
    for (var column = 0; column < 8; ++column) {
      var at = filterY * width + filterX + column;
      var response =
        (plane[at] - 3 * plane[at + width] + 3 * plane[at + 2 * width] - plane[at + 3 * width] + 4) >> 3;
      var adjustment = _Limit(response, limit);

      plane[at + width] = _Clamp(plane[at + width] + adjustment);
      plane[at + 2 * width] = _Clamp(plane[at + 2 * width] - adjustment);
    }
  }

  private static byte _Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);

  /// <summary>
  /// Filters every coded block edge of a frame — section 7.10.3.
  /// </summary>
  /// <param name="frame">The reconstructed frame, modified in place.</param>
  /// <param name="geometry">Where the blocks are.</param>
  /// <param name="coded">Which blocks were coded, indexed in coded order.</param>
  /// <param name="limit">The limit value for this frame's first quantisation index.</param>
  internal static void Apply(TheoraFrame frame, TheoraGeometry geometry, bool[] coded, int limit) {
    // A limit of zero makes every response zero, so the whole pass is a no-op. Skipped rather than
    // run, because a quiet stream sets it for whole frames at a time.
    if (limit == 0)
      return;

    for (var plane = 0; plane < 3; ++plane) {
      var samples = frame.Planes[plane];
      var width = frame.Widths[plane];
      var height = frame.Heights[plane];
      var blocksWide = geometry.PlaneBlocksWide[plane];
      var blocksHigh = geometry.PlaneBlocksHigh[plane];

      // Raster order, not coded order. Each filter application changes samples the next one reads,
      // so the order is part of the answer.
      for (var row = 0; row < blocksHigh; ++row)
      for (var column = 0; column < blocksWide; ++column) {
        var block = geometry.BlockAt(plane, column, row);
        if (!coded[block])
          continue;

        var x = column * TheoraGeometry.BLOCK_SIZE;
        var y = row * TheoraGeometry.BLOCK_SIZE;

        if (x > 0)
          _FilterHorizontally(samples, width, x - 2, y, limit);

        if (y > 0)
          _FilterVertically(samples, width, x, y - 2, limit);

        // The right and top edges only where the neighbour is uncoded, because a coded neighbour
        // will filter this same edge as its own left or bottom one.
        if (x + 8 < width && !coded[geometry.BlockAt(plane, column + 1, row)])
          _FilterHorizontally(samples, width, x + 6, y, limit);

        if (y + 8 < height && !coded[geometry.BlockAt(plane, column, row + 1)])
          _FilterVertically(samples, width, x, y + 6, limit);
      }
    }
  }
}
