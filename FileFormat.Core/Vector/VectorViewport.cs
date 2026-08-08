using System;

namespace FileFormat.Core.Vector;

/// <summary>
/// The pixel size a drawing is rendered at, and the transform from its own coordinates onto it.
/// </summary>
/// <remarks>
/// A drawing has no pixels of its own, so a size has to be decided and the decision has to be
/// stated. Every format here says how big its page is one way or another — a VDC extent, a pair of
/// scaling points, a bounding box in points, a page in tenths of a millimetre — and this turns any
/// of those into a raster the same way: the drawing's own extent fills the picture, at the file's
/// own aspect ratio, capped so no file can ask for a surface that will not fit in memory.
/// <para/>
/// The alternative, a fixed size for everything, throws away the one thing the file did state.
/// </remarks>
public readonly record struct VectorViewport(int Width, int Height, Matrix2D Transform) {

  /// <summary>The pixels per inch a drawing measured in physical units is rendered at.</summary>
  /// <remarks>
  /// Ninety-six, which is the nominal pixel density of a screen and what an inch means to every
  /// browser and to ImageMagick's default for SVG. It is a convention rather than a measurement,
  /// but it is the convention the tools this is compared against use.
  /// </remarks>
  public const double DefaultDotsPerInch = 96;

  /// <summary>Millimetres to an inch, for the formats that state a page in metric units.</summary>
  public const double MillimetresPerInch = 25.4;

  /// <summary>PostScript points to an inch.</summary>
  public const double PointsPerInch = 72;

  /// <summary>The longest side a rendered drawing is given when its own size has to be capped.</summary>
  public const int MaximumSide = 4096;

  /// <summary>The longest side a drawing is given when the file states no size at all.</summary>
  public const int FallbackSide = 640;

  /// <summary>
  /// Fits a drawing whose extent is known in its own units into a raster of the given pixel size.
  /// </summary>
  /// <param name="minX">The left of the drawing's own extent.</param>
  /// <param name="minY">One edge of the drawing's own extent along y.</param>
  /// <param name="maxX">The right of the drawing's own extent.</param>
  /// <param name="maxY">The other edge of the drawing's own extent along y.</param>
  /// <param name="width">How many pixels wide the result is.</param>
  /// <param name="height">How many pixels tall the result is.</param>
  /// <param name="flipY">
  /// Whether y grows upwards in the drawing, as it does in PostScript, HP-GL and CGM, and so has to
  /// be turned over for a raster whose first row is the top.
  /// </param>
  public static VectorViewport Fit(double minX, double minY, double maxX, double maxY, int width, int height, bool flipY) {
    if (width < 1 || height < 1)
      throw new ArgumentOutOfRangeException(nameof(width), $"A viewport of {width}x{height} has no pixels.");

    var spanX = maxX - minX;
    var spanY = maxY - minY;
    if (!double.IsFinite(spanX) || !double.IsFinite(spanY) || spanX == 0 || spanY == 0)
      throw new ArgumentOutOfRangeException(nameof(maxX), $"A drawing extent of {spanX} by {spanY} cannot be rendered.");

    var scaleX = width / spanX;
    var scaleY = height / spanY;

    var transform = Matrix2D.Translation(-minX, -minY).Then(Matrix2D.Scaling(scaleX, flipY ? -scaleY : scaleY));
    if (flipY)
      transform = transform.Then(Matrix2D.Translation(0, height));

    return new(width, height, transform);
  }

  /// <summary>
  /// Fits a drawing into the largest raster of its own aspect ratio that the cap allows.
  /// </summary>
  /// <param name="preferredWidth">How wide the file says it is, in pixels; may exceed the cap.</param>
  /// <param name="preferredHeight">How tall the file says it is, in pixels; may exceed the cap.</param>
  public static VectorViewport FitCapped(double minX, double minY, double maxX, double maxY, double preferredWidth, double preferredHeight, bool flipY) {
    var (width, height) = Cap(preferredWidth, preferredHeight);
    return Fit(minX, minY, maxX, maxY, width, height, flipY);
  }

  /// <summary>
  /// Rounds a size in pixels to whole pixels, keeping its shape, and brings it inside the cap.
  /// </summary>
  public static (int Width, int Height) Cap(double width, double height) {
    if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
      return (FallbackSide, FallbackSide);

    var longest = Math.Max(width, height);
    if (longest > MaximumSide) {
      var scale = MaximumSide / longest;
      width *= scale;
      height *= scale;
    }

    return (Math.Max(1, (int)Math.Round(width)), Math.Max(1, (int)Math.Round(height)));
  }

  /// <summary>How many pixels a length in millimetres comes to at the default density.</summary>
  public static double PixelsFromMillimetres(double millimetres) => millimetres / MillimetresPerInch * DefaultDotsPerInch;

  /// <summary>How many pixels a length in PostScript points comes to at the default density.</summary>
  public static double PixelsFromPoints(double points) => points / PointsPerInch * DefaultDotsPerInch;
}
