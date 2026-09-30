using System;
using FileFormat.Core;

namespace Hawkynt.ImageProcessing;

/// <summary>
/// A picture opened up for arithmetic: one <see cref="float"/> per component, normalised so that
/// black is 0 and full scale is 1, interleaved, colour components first and alpha — where the
/// picture has one — last.
/// </summary>
/// <remarks>
/// Every operation in this assembly is arithmetic on component values, and doing that arithmetic in
/// the storage layout would mean writing it once per layout. One working buffer means one copy of
/// each operation; the price is a read and a write per call, which is nothing next to a convolution
/// or a resample.
/// <para/>
/// Normalised floats rather than the source's integers, because the intermediate values do not fit
/// the source's range: a sharpen kernel overshoots both ends, a resample's spline undershoots below
/// zero, and rounding those back into 8 bits between passes is exactly how a two-pass separable
/// filter acquires banding. Clamping happens once, on the way out.
/// <para/>
/// The buffer also remembers the layout it came from, so an operation that does not change the
/// meaning of the components can put them back the way it found them rather than handing every
/// caller 32-bit RGBA.
/// </remarks>
internal sealed class PixelSurface {

  private PixelSurface(
    int width, int height, int channels, bool hasAlpha, bool wide, PixelFormat writeback, float[] samples) {
    this.Width = width;
    this.Height = height;
    this.Channels = channels;
    this.HasAlpha = hasAlpha;
    this.Wide = wide;
    this.Writeback = writeback;
    this.Samples = samples;
  }

  /// <summary>Creates an all-zero surface of a given size and shape.</summary>
  public PixelSurface(int width, int height, int channels, bool hasAlpha, bool wide, PixelFormat writeback)
    : this(width, height, channels, hasAlpha, wide, writeback, new float[checked(width * height * channels)]) { }

  public int Width { get; }
  public int Height { get; }

  /// <summary>Components per pixel: 1 grey, 2 grey and alpha, 3 colour, 4 colour and alpha.</summary>
  public int Channels { get; }

  /// <summary>Whether the last component is alpha rather than a colour.</summary>
  public bool HasAlpha { get; }

  /// <summary>Whether the source carried more than eight bits per component.</summary>
  public bool Wide { get; }

  /// <summary>The layout this surface should be written back as.</summary>
  public PixelFormat Writeback { get; }

  /// <summary>The samples themselves, <c>Width * Height * Channels</c> of them.</summary>
  public float[] Samples { get; }

  /// <summary>Components that carry colour rather than transparency.</summary>
  public int ColorChannels => this.HasAlpha ? this.Channels - 1 : this.Channels;

  /// <summary>Whether the colour is a single grey component.</summary>
  public bool IsGrayscale => this.ColorChannels == 1;

  /// <summary>Index of the first component of a pixel.</summary>
  public int Offset(int x, int y) => (y * this.Width + x) * this.Channels;

  /// <summary>An empty surface of the same shape, at a possibly different size.</summary>
  public PixelSurface Like(int width, int height)
    => new(width, height, this.Channels, this.HasAlpha, this.Wide, this.Writeback);

  /// <summary>An empty surface of the same shape and size.</summary>
  public PixelSurface Like() => this.Like(this.Width, this.Height);

  /// <summary>A copy of this surface.</summary>
  public PixelSurface Clone() {
    var copy = this.Like();
    Array.Copy(this.Samples, copy.Samples, this.Samples.Length);
    return copy;
  }

  /// <summary>
  /// One component of one pixel, with coordinates clamped to the edge.
  /// </summary>
  /// <remarks>
  /// Clamping rather than wrapping or a fixed background, because that is what every
  /// implementation these operations are measured against does off the edge: ImageMagick's
  /// default virtual pixel is <c>edge</c>, and a wrap would drag the far side of the picture into
  /// a blur of the near one.
  /// </remarks>
  public float At(int x, int y, int channel) {
    if (x < 0)
      x = 0;
    else if (x >= this.Width)
      x = this.Width - 1;

    if (y < 0)
      y = 0;
    else if (y >= this.Height)
      y = this.Height - 1;

    return this.Samples[(y * this.Width + x) * this.Channels + channel];
  }
}
