using System;
using FileFormat.Core;

namespace Hawkynt.ImageProcessing;

/// <summary>
/// Changing how many pixels a picture is made of. Four verbs, because they are four different
/// questions even though one filter answers all of them.
/// </summary>
/// <remarks>
/// <para><b>Resize</b> takes a pixel count. <b>Scale</b> takes a factor. <b>Fit</b> takes a box and
/// keeps the aspect ratio. <b>Resample</b> takes a sample density and keeps the physical size, which
/// is the only one of the four that changes the picture's metadata rather than just its pixels.
/// <b>Regenerate</b> keeps the size and runs the filter anyway; only a non-interpolating kernel
/// changes anything that way, and <see cref="Regenerate"/> says which.</para>
/// <para><b>Sampling convention.</b> A destination pixel's centre maps to
/// <c>(x + 0.5) / factor</c> in source pixel-edge coordinates, and the filter is evaluated against
/// the distance from there to each source pixel's centre. Sampling from centres rather than from
/// edges is what makes a factor of one land back on the original grid instead of half a pixel off
/// it, and it is what every implementation worth agreeing with does.</para>
/// <para><b>Reduction widens the filter.</b> Shrinking by a factor of <c>f</c> stretches the
/// filter's reach by <c>1/f</c>, so that every source pixel the destination pixel covers is read.
/// Without that, a reduction to a quarter size would read one pixel in four and alias; with it, the
/// filter is a low-pass of the right width.</para>
/// <para><b>Transparency.</b> Where the picture has an alpha channel, colour components are weighted
/// by alpha and then divided by the summed alpha weight, and the alpha channel itself is weighted
/// plainly. That is the difference between a resample that is correct at a transparent edge and one
/// that drags the colour of invisible pixels into visible ones — usually as a dark halo, because a
/// fully transparent pixel is so often stored as transparent black.</para>
/// <para><b>Palette pictures.</b> Only <see cref="ResampleKernel.Nearest"/> can keep one indexed,
/// and it does: it copies indices, so the palette and its transparency table come out untouched.
/// Every other kernel mixes neighbouring colours, which a fixed palette has no entry for, so the
/// result comes back as RGB.</para>
/// </remarks>
public static class ImageScaler {

  /// <summary>
  /// The nudge that decides a boundary case, and the reason it exists rather than being cleaned up.
  /// </summary>
  /// <remarks>
  /// A destination centre can land exactly on a source pixel's edge, and then the window's far end
  /// falls exactly on an integer too. Which way that rounds decides whether one more source pixel
  /// contributes, and the answer differs between implementations that do and do not nudge. This is
  /// the same nudge, in the same direction, as the implementation these results are measured
  /// against — an arbitrary-but-agreed value, and matching it is the specification.
  /// </remarks>
  private const double _EPSILON = 1.0e-12;

  /// <summary>Resizes a picture to an exact pixel size, ignoring its aspect ratio.</summary>
  public static RawImage Resize(RawImage image, int width, int height, ResampleKernel kernel) {
    ArgumentNullException.ThrowIfNull(image);
    if (width < 1)
      throw new ArgumentOutOfRangeException(nameof(width), width, "A picture needs at least one pixel across.");
    if (height < 1)
      throw new ArgumentOutOfRangeException(nameof(height), height, "A picture needs at least one row.");

    if (width == image.Width && height == image.Height)
      return image;

    return _Resample(image, width, height, kernel, false);
  }

  /// <summary>Scales a picture by one factor in both directions.</summary>
  public static RawImage Scale(RawImage image, double factor, ResampleKernel kernel)
    => Scale(image, factor, factor, kernel);

  /// <summary>Scales a picture by a factor per direction.</summary>
  public static RawImage Scale(RawImage image, double horizontal, double vertical, ResampleKernel kernel) {
    ArgumentNullException.ThrowIfNull(image);
    if (horizontal <= 0.0)
      throw new ArgumentOutOfRangeException(nameof(horizontal), horizontal, "A scale factor has to be positive.");
    if (vertical <= 0.0)
      throw new ArgumentOutOfRangeException(nameof(vertical), vertical, "A scale factor has to be positive.");

    return Resize(image, _Round(image.Width * horizontal), _Round(image.Height * vertical), kernel);
  }

  /// <summary>
  /// Scales a picture to the largest size that fits inside a box without changing its aspect ratio.
  /// </summary>
  /// <remarks>
  /// A picture already inside the box is enlarged to touch it. A caller that wants shrink-only
  /// behaviour should compare the sizes first; baking that decision in here would make the operation
  /// two operations wearing one name.
  /// </remarks>
  public static RawImage Fit(RawImage image, int boundsWidth, int boundsHeight, ResampleKernel kernel) {
    ArgumentNullException.ThrowIfNull(image);
    if (boundsWidth < 1)
      throw new ArgumentOutOfRangeException(nameof(boundsWidth), boundsWidth, "A box needs at least one pixel across.");
    if (boundsHeight < 1)
      throw new ArgumentOutOfRangeException(nameof(boundsHeight), boundsHeight, "A box needs at least one row.");

    var factor = Math.Min((double)boundsWidth / image.Width, (double)boundsHeight / image.Height);
    return Resize(image, _Round(image.Width * factor), _Round(image.Height * factor), kernel);
  }

  /// <summary>
  /// Changes how densely a picture is sampled while keeping the physical size it claims.
  /// </summary>
  /// <param name="image">The picture to resample. It has to say what density it is at now.</param>
  /// <param name="dpiX">The horizontal density to resample to, in dots per inch.</param>
  /// <param name="dpiY">The vertical density to resample to, in dots per inch.</param>
  /// <param name="kernel">The filter to read the source through.</param>
  /// <remarks>
  /// This is the operation people mean by resampling as opposed to resizing: the picture stays the
  /// same number of inches wide and changes how many pixels that is. It therefore needs to know the
  /// density it is at, and a picture whose metadata does not say is refused rather than assumed to
  /// be at 72 or 96 — an assumed input density makes the output size wrong by exactly the ratio of
  /// the guess to the truth, silently.
  /// </remarks>
  public static RawImage Resample(RawImage image, double dpiX, double dpiY, ResampleKernel kernel) {
    ArgumentNullException.ThrowIfNull(image);
    if (dpiX <= 0.0)
      throw new ArgumentOutOfRangeException(nameof(dpiX), dpiX, "A sample density has to be positive.");
    if (dpiY <= 0.0)
      throw new ArgumentOutOfRangeException(nameof(dpiY), dpiY, "A sample density has to be positive.");

    var sourceX = image.Metadata?.DpiX;
    var sourceY = image.Metadata?.DpiY;
    if (sourceX is not > 0.0 || sourceY is not > 0.0)
      throw new ArgumentException(
        "The picture does not say what density it is sampled at, so there is nothing to resample from.",
        nameof(image));

    var resized = Resize(
      image, _Round(image.Width * dpiX / sourceX.Value), _Round(image.Height * dpiY / sourceY.Value), kernel);

    return _WithDensity(resized, dpiX, dpiY);
  }

  /// <summary>
  /// Runs the filter over the picture at the size it already is.
  /// </summary>
  /// <remarks>
  /// Every kernel reconstructs a continuous picture and samples it again. At a factor of one the
  /// samples land exactly on the source's, so an interpolating kernel — Nearest, Box, Bilinear,
  /// Bicubic and Lanczos all pass through their samples — gives the picture back unchanged, and only
  /// <see cref="ResampleKernel.Mitchell"/>, which trades exact interpolation for less ringing,
  /// changes it: it softens slightly. That is why this is separate from <see cref="Resize"/>, which
  /// treats "resize to the size it already has" as "leave it alone": the one kernel for which the
  /// distinction matters is also the one a caller would reach for on purpose.
  /// </remarks>
  public static RawImage Regenerate(RawImage image, ResampleKernel kernel) {
    ArgumentNullException.ThrowIfNull(image);
    return _Resample(image, image.Width, image.Height, kernel, true);
  }

  private static RawImage _Resample(RawImage image, int width, int height, ResampleKernel kernel, bool force) {
    if (image.IsIndexed && kernel == ResampleKernel.Nearest)
      return IndexedSampler.Nearest(image, width, height);

    var filter = ResampleWeighting.For(kernel);
    var surface = PixelSurfaces.Read(image);

    if (force || surface.Width != width)
      surface = _Along(surface, width, true, filter);
    if (force || surface.Height != height)
      surface = _Along(surface, height, false, filter);

    return PixelSurfaces.Write(surface, image);
  }

  /// <summary>Resamples along one axis, leaving the other alone.</summary>
  private static PixelSurface _Along(PixelSurface source, int target, bool horizontal, ResampleWeighting filter) {
    var from = horizontal ? source.Width : source.Height;
    var factor = (double)target / from;

    // Reduction stretches the filter's reach and divides the distance it is evaluated at by the
    // same amount, which is what turns a reconstruction filter into a low-pass of the right width.
    var scale = Math.Max(1.0 / factor + _EPSILON, 1.0);
    var support = scale * filter.Support;
    if (support < 0.5) {
      support = 0.5;
      scale = 1.0;
    }

    scale = 1.0 / scale;

    var result = horizontal ? source.Like(target, source.Height) : source.Like(source.Width, target);
    var channels = source.Channels;
    var colors = source.ColorChannels;
    var lines = horizontal ? source.Height : source.Width;
    var lineStride = horizontal ? source.Width * channels : channels;
    var sampleStride = horizontal ? channels : source.Width * channels;
    var targetLineStride = horizontal ? target * channels : channels;
    var targetSampleStride = horizontal ? channels : result.Width * channels;
    var samples = source.Samples;
    var output = result.Samples;
    var weights = new double[(int)(2.0 * support + 4.0)];

    // Outside the loop: a stackalloc inside one is not freed until the method returns.
    Span<double> accumulator = stackalloc double[4];

    for (var i = 0; i < target; ++i) {
      var bisect = (i + 0.5) / factor + _EPSILON;
      var start = (int)Math.Max(bisect - support + 0.5, 0.0);
      var stop = (int)Math.Min(bisect + support + 0.5, from);
      var count = stop - start;
      if (count <= 0)
        continue;

      var density = 0.0;
      for (var n = 0; n < count; ++n) {
        weights[n] = filter.Weight(scale * (start + n - bisect + 0.5));
        density += weights[n];
      }

      // Normalising is what keeps a truncated filter from darkening the edges of the picture: the
      // lobes cut off outside the support took some of the unit weight with them.
      if (density != 0.0 && density != 1.0) {
        var reciprocal = 1.0 / density;
        for (var n = 0; n < count; ++n)
          weights[n] *= reciprocal;
      }

      for (var line = 0; line < lines; ++line) {
        var first = line * lineStride + start * sampleStride;
        accumulator.Clear();
        var alpha = 0.0;
        var coverage = 0.0;

        if (source.HasAlpha)
          for (var n = 0; n < count; ++n) {
            var at = first + n * sampleStride;
            var weight = weights[n];
            var weighted = weight * samples[at + colors];
            for (var channel = 0; channel < colors; ++channel)
              accumulator[channel] += weighted * samples[at + channel];

            coverage += weighted;
            alpha += weight * samples[at + colors];
          }
        else
          for (var n = 0; n < count; ++n) {
            var at = first + n * sampleStride;
            var weight = weights[n];
            for (var channel = 0; channel < colors; ++channel)
              accumulator[channel] += weight * samples[at + channel];
          }

        var to = line * targetLineStride + i * targetSampleStride;
        if (source.HasAlpha) {
          var reciprocal = Math.Abs(coverage) < _EPSILON ? 0.0 : 1.0 / coverage;
          for (var channel = 0; channel < colors; ++channel)
            output[to + channel] = (float)(accumulator[channel] * reciprocal);

          output[to + colors] = (float)alpha;
        } else
          for (var channel = 0; channel < colors; ++channel)
            output[to + channel] = (float)accumulator[channel];
      }
    }

    return result;
  }

  private static RawImage _WithDensity(RawImage image, double dpiX, double dpiY) {
    var metadata = image.Metadata;
    return new() {
      Width = image.Width,
      Height = image.Height,
      Format = image.Format,
      PixelData = image.PixelData,
      Palette = image.Palette,
      PaletteCount = image.PaletteCount,
      AlphaTable = image.AlphaTable,
      ColorInfo = image.ColorInfo,
      CfaInfo = image.CfaInfo,
      Metadata = new() {
        Exif = metadata?.Exif,
        XmpPacket = metadata?.XmpPacket,
        Iptc = metadata?.Iptc,
        IccProfile = metadata?.IccProfile,
        IccProfileName = metadata?.IccProfileName,
        TextEntries = metadata?.TextEntries ?? [],
        DpiX = dpiX,
        DpiY = dpiY,
      },
    };
  }

  private static int _Round(double value) {
    var pixels = (int)Math.Round(value, MidpointRounding.AwayFromZero);
    return pixels < 1 ? 1 : pixels;
  }
}
