namespace FileFormat.Codecs.Theora;

/// <summary>
/// One decoded frame: the three sample planes, in Theora's own coordinates.
/// </summary>
/// <remarks>
/// Row zero is the *bottom* row. Theora uses a right-handed coordinate system with the origin in the
/// lower-left corner, unlike most video formats and unlike every bitmap this library writes, and
/// every position in the decoder — block coordinates, motion vectors, filter edges — is in those
/// coordinates. Storing the planes the same way up means the decode never flips anything; the one
/// flip happens when a picture is handed out, in <see cref="TheoraColorConversion"/>.
/// <para/>
/// The planes are the whole coded frame rather than the picture region. The parts outside the
/// picture hold real coded samples that later frames predict from, so they are decoded like any
/// other and cropped away only at the end.
/// </remarks>
internal sealed class TheoraFrame {

  internal required byte[][] Planes { get; init; }

  internal required int[] Widths { get; init; }

  internal required int[] Heights { get; init; }

  internal static TheoraFrame Create(TheoraGeometry geometry) {
    var planes = new byte[3][];
    var widths = new int[3];
    var heights = new int[3];

    for (var plane = 0; plane < 3; ++plane) {
      widths[plane] = geometry.PlaneWidth[plane];
      heights[plane] = geometry.PlaneHeight[plane];
      planes[plane] = new byte[widths[plane] * heights[plane]];
    }

    return new() { Planes = planes, Widths = widths, Heights = heights };
  }

  /// <summary>Copies another frame's samples into this one.</summary>
  internal void CopyFrom(TheoraFrame other) {
    for (var plane = 0; plane < 3; ++plane)
      other.Planes[plane].CopyTo(this.Planes[plane], 0);
  }
}
