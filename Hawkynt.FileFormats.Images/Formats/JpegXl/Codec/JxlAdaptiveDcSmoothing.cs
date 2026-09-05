using System;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// Smooths a frame's low-frequency image before the rest of it is rebuilt on
/// top (libjxl <c>compressed_dc.cc::AdaptiveDCSmoothing</c>).
/// </summary>
/// <remarks>
/// A frame carries one value per block for the low frequencies, and quantising
/// those leaves visible steps between neighbouring blocks in anything smooth.
/// The encoder counts on the decoder taking them out again, so this is not an
/// improvement a decoder may apply or skip: a frame is written expecting it,
/// and every frame gets it unless the frame says otherwise.
///
/// <para>What makes it adaptive is that it stops where the picture is genuinely
/// changing. Each value is compared with the average of its neighbours, and how
/// far apart they are is measured in whole quantisation steps rather than in
/// levels — so a difference the quantiser could not have introduced is left
/// alone, and only what looks like quantisation noise is smoothed away. The
/// three planes decide together, on whichever of them disagrees most, so a
/// colour edge is not smoothed out of one plane while another keeps it.</para>
/// </remarks>
internal static class JxlAdaptiveDcSmoothing {

  private const float _Side = 0.20345139757231578f;
  private const float _Corner = 0.0334829185968739f;
  private static readonly float _Centre = 1.0f - 4.0f * (_Side + _Corner);

  /// <summary>
  /// Smooth the low-frequency image in place.
  /// </summary>
  /// <param name="planes">The three planes, one value per block, laid out
  /// together as <c>channel * count + index</c>.</param>
  /// <param name="width">Blocks across.</param>
  /// <param name="height">Blocks down.</param>
  /// <param name="stepPerChannel">The quantisation step each plane's values
  /// were taken in, which is what the difference is measured against.</param>
  public static void Apply(float[] planes, int width, int height, float[] stepPerChannel) {
    ArgumentNullException.ThrowIfNull(planes);
    ArgumentNullException.ThrowIfNull(stepPerChannel);
    if (stepPerChannel.Length < 3)
      throw new ArgumentException("A step is needed for each of the three planes.", nameof(stepPerChannel));

    // With two rows or two columns there is no interior to smooth.
    if (width <= 2 || height <= 2)
      return;

    var count = width * height;
    if (planes.Length < 3 * count)
      throw new ArgumentException("The planes are shorter than the picture they are said to cover.", nameof(planes));

    // The edges keep what they had; everything else is written fresh, so the
    // pass reads only the values it started with.
    var smoothed = new float[3 * count];
    Array.Copy(planes, smoothed, smoothed.Length);

    var averages = new float[3];
    for (var y = 1; y < height - 1; ++y)
    for (var x = 1; x < width - 1; ++x) {
      var at = y * width + x;

      // How far the value is from its neighbourhood, in quantisation steps,
      // taken over whichever plane disagrees most.
      var gap = 0.5f;
      for (var c = 0; c < 3; ++c) {
        var plane = c * count;
        var corners = planes[plane + at - width - 1] + planes[plane + at - width + 1]
                      + planes[plane + at + width - 1] + planes[plane + at + width + 1];
        var sides = planes[plane + at - 1] + planes[plane + at + 1]
                    + planes[plane + at - width] + planes[plane + at + width];
        var average = corners * _Corner + sides * _Side + planes[plane + at] * _Centre;
        averages[c] = average;

        var step = stepPerChannel[c];
        if (step == 0.0f)
          continue;

        var apart = Math.Abs((planes[plane + at] - average) / step);
        if (apart > gap)
          gap = apart;
      }

      // Smoothed in full while the gap stays under half a step, tapering to
      // nothing by three quarters of one.
      var strength = 3.0f - 4.0f * gap;
      if (strength <= 0.0f)
        continue;

      for (var c = 0; c < 3; ++c) {
        var plane = c * count;
        smoothed[plane + at] = planes[plane + at] + (averages[c] - planes[plane + at]) * strength;
      }
    }

    Array.Copy(smoothed, planes, smoothed.Length);
  }
}
