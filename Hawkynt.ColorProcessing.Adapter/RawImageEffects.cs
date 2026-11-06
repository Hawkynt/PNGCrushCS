using FileFormat.Core;
using Hawkynt.ColorProcessing.Filtering.Filters;
using Hawkynt.ColorProcessing.Resizing.Rescalers;
using EagleRule = Hawkynt.ColorProcessing.Resizing.Rescalers.Eagle;
using XbrRule = Hawkynt.ColorProcessing.Resizing.Rescalers.Xbr;

namespace Hawkynt.ColorProcessing.Adapter;

/// <summary>
/// The colour library's effects and pixel-art doublers by name, for callers that should not have to
/// name the library's own types.
/// </summary>
/// <remarks>
/// Each of these is one line over <see cref="RawImageFiltering"/>, and that is the point of it: an
/// application gets the operations without a compile-time reference to the colour library, which it
/// could otherwise only take by the hint path this project already carries. What a parameter means,
/// and the range it is clamped to, is the library's; the summaries below say what it said.
/// </remarks>
public static class RawImageEffects {

  /// <summary>Smears the picture along a line, as a moving camera would.</summary>
  /// <param name="image">The picture to blur.</param>
  /// <param name="length">How many pixels the smear covers; at least one.</param>
  /// <param name="angle">The direction of the smear in degrees, 0 being horizontal.</param>
  public static RawImage MotionBlur(RawImage image, int length, double angle)
    => RawImageFiltering.Filter(image, new MotionBlur(length, (float)angle));

  /// <summary>Replaces each pixel with the mean of the commonest brightness band around it.</summary>
  /// <param name="image">The picture to paint.</param>
  /// <param name="radius">How far the neighbourhood reaches on each side; at least one.</param>
  /// <param name="levels">How many brightness bands the neighbourhood is sorted into; at least two.</param>
  public static RawImage OilPainting(RawImage image, int radius, int levels)
    => RawImageFiltering.Filter(image, new OilPainting(radius, levels));

  /// <summary>Averages the picture into square blocks.</summary>
  /// <param name="image">The picture to pixelate.</param>
  /// <param name="blockSize">The side of one block in pixels; at least one.</param>
  public static RawImage Pixelate(RawImage image, int blockSize)
    => RawImageFiltering.Filter(image, new Pixelate(blockSize));

  /// <summary>Rounds every colour component to one of a few evenly spaced levels.</summary>
  /// <param name="image">The picture to posterise.</param>
  /// <param name="levels">How many levels each component keeps, black and full scale among them.</param>
  public static RawImage Posterize(RawImage image, int levels)
    => RawImageFiltering.Filter(image, new Posterize(levels));

  /// <summary>Inverts every colour component that is above a threshold.</summary>
  /// <param name="image">The picture to solarise.</param>
  /// <param name="threshold">The level above which a component is inverted, from 0 to 1.</param>
  public static RawImage Solarize(RawImage image, double threshold)
    => RawImageFiltering.Filter(image, new Solarize((float)threshold));

  /// <summary>Tints the picture brown through the usual sepia matrix.</summary>
  /// <param name="image">The picture to tint.</param>
  /// <param name="intensity">How far to go from the picture to its sepia version, from 0 to 1.</param>
  public static RawImage Sepia(RawImage image, double intensity)
    => RawImageFiltering.Filter(image, new Sepia((float)intensity));

  /// <summary>Maps the tonal range onto a gradient between two colours.</summary>
  /// <param name="image">The picture to tint.</param>
  /// <param name="shadowRed">The shadow colour's red, from 0 to 1.</param>
  /// <param name="shadowGreen">The shadow colour's green, from 0 to 1.</param>
  /// <param name="shadowBlue">The shadow colour's blue, from 0 to 1.</param>
  /// <param name="highlightRed">The highlight colour's red, from 0 to 1.</param>
  /// <param name="highlightGreen">The highlight colour's green, from 0 to 1.</param>
  /// <param name="highlightBlue">The highlight colour's blue, from 0 to 1.</param>
  public static RawImage Duotone(
    RawImage image,
    double shadowRed, double shadowGreen, double shadowBlue,
    double highlightRed, double highlightGreen, double highlightBlue)
    => RawImageFiltering.Filter(image, new Duotone(
      (float)shadowRed, (float)shadowGreen, (float)shadowBlue,
      (float)highlightRed, (float)highlightGreen, (float)highlightBlue));

  /// <summary>Shifts the three colour axes against one another, separately for each tonal range.</summary>
  /// <param name="image">The picture to balance.</param>
  /// <param name="shadows">Cyan to red, magenta to green and yellow to blue in the shadows, each from -1 to 1.</param>
  /// <param name="midtones">The same three shifts in the midtones.</param>
  /// <param name="highlights">The same three shifts in the highlights.</param>
  public static RawImage ColorBalance(
    RawImage image,
    (double CyanRed, double MagentaGreen, double YellowBlue) shadows,
    (double CyanRed, double MagentaGreen, double YellowBlue) midtones,
    (double CyanRed, double MagentaGreen, double YellowBlue) highlights)
    => RawImageFiltering.Filter(image, new ColorBalance(
      (float)shadows.CyanRed, (float)shadows.MagentaGreen, (float)shadows.YellowBlue,
      (float)midtones.CyanRed, (float)midtones.MagentaGreen, (float)midtones.YellowBlue,
      (float)highlights.CyanRed, (float)highlights.MagentaGreen, (float)highlights.YellowBlue));

  /// <summary>
  /// Averages each pixel with the neighbours whose colour is close to its own, which smooths noise
  /// and leaves an edge where it is.
  /// </summary>
  /// <param name="image">The picture to clean.</param>
  /// <param name="strength">
  /// How different a neighbour's colour may be and still count, as a distance in normalised RGB.
  /// </param>
  /// <param name="radius">How far the neighbourhood reaches on each side; at least one.</param>
  public static RawImage ReduceNoise(RawImage image, double strength, int radius)
    => RawImageFiltering.Filter(image, new ReduceNoise((float)strength, radius));

  /// <summary>Doubles a picture by the Scale2x rule.</summary>
  public static RawImage Scale2x(RawImage image) => RawImageFiltering.Upscale(image, Scale.Default);

  /// <summary>Doubles a picture by the Eagle rule.</summary>
  public static RawImage Eagle(RawImage image) => RawImageFiltering.Upscale(image, EagleRule.Default);

  /// <summary>Doubles a picture by xBR.</summary>
  public static RawImage Xbr(RawImage image) => RawImageFiltering.Upscale(image, XbrRule.Default);
}
