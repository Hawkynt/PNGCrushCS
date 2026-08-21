namespace FileFormat.Codecs.Vp3;

/// <summary>
/// The deblocking filter of Section 7.10, run over every coded block edge before the frame becomes a
/// reference.
/// </summary>
/// <remarks>
/// It is a four-tap edge detector across each block boundary, whose response is fed through a
/// limiting function and then added to the sample on one side and subtracted from the sample on the
/// other. The limit rises to a peak at the limit value and falls back to zero at twice it, so a small
/// step across the edge — which is what a quantisation artefact looks like — is smoothed away, while
/// a large one, which is a real edge in the picture, is left alone. The limit value comes from
/// <see cref="Vp3Tables.LoopFilterLimits"/> indexed by the frame's quantisation index, and at the
/// finest quantisers it is zero, which turns the filter off.
/// <para/>
/// The order the edges are filtered in matters and is not a detail. Each filter writes back into the
/// picture the next one reads, so filtering a block's left edge before its bottom edge gives
/// different samples than the other way round; the order here — for each coded block in raster order,
/// left edge, bottom edge, then the right and top edges only where the neighbour on that side is not
/// coded — is VP3's, and any other produces a picture that drifts.
/// <para/>
/// The right and top edges are conditional because an edge between two coded blocks would otherwise
/// be filtered twice: once as the right edge of one and once as the left edge of the next. Filtering
/// it once, from the later block, is what the unconditional left and bottom cases already do.
/// </remarks>
internal static class Vp3LoopFilter {

  internal static void Apply(Vp3Frame frame, Vp3Geometry geometry, bool[] coded, int quantisationIndex) {
    var limit = Vp3Tables.LoopFilterLimits[quantisationIndex];
    if (limit == 0)
      return;

    for (var plane = 0; plane < 3; ++plane) {
      var samples = frame.Plane(plane);
      var width = geometry.PlaneWidth[plane];
      var height = geometry.PlaneHeight[plane];
      var blockColumns = geometry.PlaneBlockWidth[plane];
      var blockRows = geometry.PlaneBlockHeight[plane];
      var index = geometry.CodedIndex[plane];

      for (var blockRow = 0; blockRow < blockRows; ++blockRow)
      for (var blockColumn = 0; blockColumn < blockColumns; ++blockColumn) {
        if (!coded[index[blockRow * blockColumns + blockColumn]])
          continue;

        var x = blockColumn * 8;
        var y = blockRow * 8;

        if (x > 0)
          _Horizontal(samples, width, x - 2, y, limit);

        if (y > 0)
          _Vertical(samples, width, x, y - 2, limit);

        if (x + 8 < width && !coded[index[blockRow * blockColumns + blockColumn + 1]])
          _Horizontal(samples, width, x + 6, y, limit);

        if (y + 8 < height && !coded[index[(blockRow + 1) * blockColumns + blockColumn]])
          _Vertical(samples, width, x, y + 6, limit);
      }
    }
  }

  /// <summary>Filters the four columns straddling a vertical edge, over the eight rows of a block.</summary>
  private static void _Horizontal(byte[] samples, int width, int x, int y, int limit) {
    for (var row = 0; row < 8; ++row) {
      var at = (y + row) * width + x;
      var response = samples[at] - 3 * samples[at + 1] + 3 * samples[at + 2] - samples[at + 3] + 4 >> 3;
      var adjustment = _Limit(response, limit);
      samples[at + 1] = _Clamp(samples[at + 1] + adjustment);
      samples[at + 2] = _Clamp(samples[at + 2] - adjustment);
    }
  }

  /// <summary>Filters the four rows straddling a horizontal edge, over the eight columns of a block.</summary>
  private static void _Vertical(byte[] samples, int width, int x, int y, int limit) {
    for (var column = 0; column < 8; ++column) {
      var at = y * width + x + column;
      var response =
        samples[at] - 3 * samples[at + width] + 3 * samples[at + 2 * width] - samples[at + 3 * width] + 4 >> 3;
      var adjustment = _Limit(response, limit);
      samples[at + width] = _Clamp(samples[at + width] + adjustment);
      samples[at + 2 * width] = _Clamp(samples[at + 2 * width] - adjustment);
    }
  }

  /// <summary>
  /// The tapered response of Section 7.10: the edge detector's output where it is small, tapering
  /// back to nothing by twice the limit.
  /// </summary>
  private static int _Limit(int response, int limit) {
    if (response <= -2 * limit)
      return 0;

    if (response <= -limit)
      return -response - 2 * limit;

    if (response < limit)
      return response;

    if (response < 2 * limit)
      return -response + 2 * limit;

    return 0;
  }

  private static byte _Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
}
