using System;

namespace Hawkynt.ImageProcessing;

/// <summary>
/// Histogram equalisation over 65 536 levels, judged either on the picture's luma or on each colour
/// component alone.
/// </summary>
/// <remarks>
/// Converted from ImageMagick's <c>EqualizeImage</c> (see <c>THIRD-PARTY-NOTICE.ImageMagick.txt</c>).
/// Two details decide whether the result matches, and both are kept: the histogram has one bin per
/// 16-bit level rather than per 8-bit one, so a result computed at higher precision is not first
/// crushed to 256 values; and the map's black point is the count of pixels at level zero, not the
/// count below the darkest level in use, so a picture with no true black does not get one invented.
/// <para/>
/// Joint equalisation builds one histogram from Rec. 709 luma of the stored components
/// (<c>0.212656 R + 0.715158 G + 0.072186 B</c>) and runs every colour component through the one
/// map it yields. That is ImageMagick's default, and the better default: the same map on all three
/// components moves tone without moving hue much, where three separate maps rebalance the colours
/// towards grey whether or not they were wrong.
/// </remarks>
internal static class Equalization {

  private const int _LEVELS = 65536;

  /// <summary>Equalises a surface's colour components in place.</summary>
  public static void Apply(PixelSurface surface, bool joint) {
    var maps = Maps(surface, joint);
    var samples = surface.Samples;
    var stride = surface.Channels;

    for (var channel = 0; channel < maps.Length; ++channel) {
      var map = maps[channel];
      if (map is null)
        continue;

      for (var at = channel; at < samples.Length; at += stride)
        samples[at] = (float)map[_Bin(samples[at])];
    }
  }

  /// <summary>
  /// The equalisation map for each colour component, indexed by 16-bit level; null for a component
  /// with nothing to spread.
  /// </summary>
  public static double[]?[] Maps(PixelSurface surface, bool joint) {
    var colors = surface.ColorChannels;
    var samples = surface.Samples;
    var stride = surface.Channels;
    var maps = new double[]?[colors];

    if (joint && colors == 3) {
      var cumulative = new double[_LEVELS];
      for (var at = 0; at < samples.Length; at += stride)
        ++cumulative[_Bin(0.212656 * samples[at] + 0.715158 * samples[at + 1] + 0.072186 * samples[at + 2])];

      var map = _Integrate(cumulative);
      for (var channel = 0; channel < colors; ++channel)
        maps[channel] = map;

      return maps;
    }

    for (var channel = 0; channel < colors; ++channel) {
      var cumulative = new double[_LEVELS];
      for (var at = channel; at < samples.Length; at += stride)
        ++cumulative[_Bin(samples[at])];

      maps[channel] = _Integrate(cumulative);
    }

    return maps;
  }

  /// <summary>Turns a histogram into the map that flattens it, or null when there is nothing to spread.</summary>
  private static double[]? _Integrate(double[] cumulative) {
    for (var level = 1; level < _LEVELS; ++level)
      cumulative[level] += cumulative[level - 1];

    var black = cumulative[0];
    var white = cumulative[_LEVELS - 1];
    if (black == white)
      return null;

    var span = 1.0 / (white - black);
    for (var level = 0; level < _LEVELS; ++level)
      cumulative[level] = (cumulative[level] - black) * span;

    return cumulative;
  }

  /// <summary>The 16-bit level a normalised component falls in, clamped to the scale first.</summary>
  public static int Bin(double value) => _Bin(value);

  private static int _Bin(double value)
    => value <= 0.0 ? 0 : value >= 1.0 ? _LEVELS - 1 : (int)(value * (_LEVELS - 1) + 0.5);
}
