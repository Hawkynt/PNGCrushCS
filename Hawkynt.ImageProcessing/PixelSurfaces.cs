using System;
using FileFormat.Core;

namespace Hawkynt.ImageProcessing;

/// <summary>
/// Opens a <see cref="RawImage"/> up for arithmetic and closes it again in the layout it came from.
/// </summary>
/// <remarks>
/// The whole-byte interleaved layouts are read and written here directly rather than through
/// <see cref="PixelConverter"/>, and the reason is precision: routing a 16-bit picture through the
/// converter's 32-bit BGRA hub would throw away half of every component before the first
/// multiplication. A layout this class does not recognise — planar YUV, a sensor mosaic, a packed
/// 10-bit or 5/6/5 word, a floating-point component — does go through that hub, and comes back as
/// plain RGB or BGRA rather than as itself. That is a documented loss, not an accident: putting a
/// blurred picture back as 4:2:0 would have to invent a chroma decimation, and putting it back as a
/// Bayer mosaic would have to throw two of every three components away.
/// </remarks>
internal static class PixelSurfaces {

  /// <summary>Reads a picture into a working surface.</summary>
  public static PixelSurface Read(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return image.Format switch {
      PixelFormat.Gray8 => _ReadNarrow(image, [0], false, PixelFormat.Gray8),
      PixelFormat.GrayAlpha16 => _ReadNarrow(image, [0, 1], true, PixelFormat.GrayAlpha16),
      PixelFormat.Rgb24 => _ReadNarrow(image, [0, 1, 2], false, PixelFormat.Rgb24),
      PixelFormat.Bgr24 => _ReadNarrow(image, [2, 1, 0], false, PixelFormat.Bgr24),
      PixelFormat.Rgba32 => _ReadNarrow(image, [0, 1, 2, 3], true, PixelFormat.Rgba32),
      PixelFormat.Bgra32 => _ReadNarrow(image, [2, 1, 0, 3], true, PixelFormat.Bgra32),
      PixelFormat.Argb32 => _ReadNarrow(image, [1, 2, 3, 0], true, PixelFormat.Argb32),
      PixelFormat.Gray16 => _ReadWide(image, [0], false, PixelFormat.Gray16),
      PixelFormat.GrayAlpha32 => _ReadWide(image, [0, 1], true, PixelFormat.GrayAlpha32),
      PixelFormat.Rgb48 => _ReadWide(image, [0, 1, 2], false, PixelFormat.Rgb48),
      PixelFormat.Rgba64 => _ReadWide(image, [0, 1, 2, 3], true, PixelFormat.Rgba64),
      _ => _ReadThroughHub(image),
    };
  }

  /// <summary>Writes a working surface back out as a picture, carrying the original's metadata.</summary>
  public static RawImage Write(PixelSurface surface, RawImage source) {
    ArgumentNullException.ThrowIfNull(surface);
    ArgumentNullException.ThrowIfNull(source);

    var pixels = surface.Wide ? _WriteWide(surface) : _WriteNarrow(surface);

    return new() {
      Width = surface.Width,
      Height = surface.Height,
      Format = surface.Writeback,
      PixelData = pixels,
      Metadata = source.Metadata,
      // The colour interpretation describes the components that were there. Once the layout has
      // changed it describes something else, so it only survives a layout that did not.
      ColorInfo = surface.Writeback == source.Format ? source.ColorInfo : null,
    };
  }

  /// <summary>Whether a picture's own layout can be read and written without changing it.</summary>
  public static bool IsNativelyHandled(PixelFormat format) => format
    is PixelFormat.Gray8 or PixelFormat.GrayAlpha16
    or PixelFormat.Rgb24 or PixelFormat.Bgr24
    or PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Argb32
    or PixelFormat.Gray16 or PixelFormat.GrayAlpha32
    or PixelFormat.Rgb48 or PixelFormat.Rgba64;

  private static PixelSurface _ReadNarrow(
    RawImage image, ReadOnlySpan<int> order, bool hasAlpha, PixelFormat writeback) {
    var channels = order.Length;
    var surface = new PixelSurface(image.Width, image.Height, channels, hasAlpha, false, writeback);
    var source = image.PixelData;
    var target = surface.Samples;
    var pixels = image.Width * image.Height;
    _RequireSamples(source.Length, checked(pixels * channels), image.Format);

    for (var i = 0; i < pixels; ++i) {
      var at = i * channels;
      for (var channel = 0; channel < channels; ++channel)
        target[at + channel] = source[at + order[channel]] * (1f / 255f);
    }

    return surface;
  }

  private static PixelSurface _ReadWide(
    RawImage image, ReadOnlySpan<int> order, bool hasAlpha, PixelFormat writeback) {
    var channels = order.Length;
    var surface = new PixelSurface(image.Width, image.Height, channels, hasAlpha, true, writeback);
    var source = image.PixelData;
    var target = surface.Samples;
    var pixels = image.Width * image.Height;
    _RequireSamples(source.Length, checked(pixels * channels * 2), image.Format);

    for (var i = 0; i < pixels; ++i) {
      var at = i * channels;
      for (var channel = 0; channel < channels; ++channel) {
        // Sixteen-bit components are stored most significant byte first throughout this codebase.
        var word = (source[(at + order[channel]) * 2] << 8) | source[(at + order[channel]) * 2 + 1];
        target[at + channel] = word * (1f / 65535f);
      }
    }

    return surface;
  }

  /// <summary>
  /// Reads a layout this class has no direct code for, by way of the shared converter's BGRA hub.
  /// </summary>
  private static PixelSurface _ReadThroughHub(RawImage image) {
    var traits = RawPixelFormats.Get(image.Format);
    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);

    // A source that cannot carry transparency should not grow a fourth component on the way
    // through: it would come back as a picture with an alpha channel the caller never had.
    return traits.CanRepresentAlpha
      ? _ReadNarrow(bgra, [2, 1, 0, 3], true, PixelFormat.Bgra32)
      : _ReadNarrow(bgra, [2, 1, 0], false, PixelFormat.Rgb24);
  }

  private static byte[] _WriteNarrow(PixelSurface surface) {
    var order = _Order(surface.Writeback);
    var channels = surface.Channels;
    var pixels = surface.Width * surface.Height;
    var target = new byte[checked(pixels * channels)];
    var source = surface.Samples;

    for (var i = 0; i < pixels; ++i) {
      var at = i * channels;
      for (var channel = 0; channel < channels; ++channel)
        target[at + order[channel]] = _ToByte(source[at + channel]);
    }

    return target;
  }

  private static byte[] _WriteWide(PixelSurface surface) {
    var order = _Order(surface.Writeback);
    var channels = surface.Channels;
    var pixels = surface.Width * surface.Height;
    var target = new byte[checked(pixels * channels * 2)];
    var source = surface.Samples;

    for (var i = 0; i < pixels; ++i) {
      var at = i * channels;
      for (var channel = 0; channel < channels; ++channel) {
        var word = _ToWord(source[at + channel]);
        target[(at + order[channel]) * 2] = (byte)(word >> 8);
        target[(at + order[channel]) * 2 + 1] = (byte)word;
      }
    }

    return target;
  }

  /// <summary>Where each surface component belongs within a pixel of the target layout.</summary>
  private static int[] _Order(PixelFormat format) => format switch {
    PixelFormat.Gray8 or PixelFormat.Gray16 => [0],
    PixelFormat.GrayAlpha16 or PixelFormat.GrayAlpha32 => [0, 1],
    PixelFormat.Rgb24 or PixelFormat.Rgb48 => [0, 1, 2],
    PixelFormat.Bgr24 => [2, 1, 0],
    PixelFormat.Rgba32 or PixelFormat.Rgba64 => [0, 1, 2, 3],
    PixelFormat.Bgra32 => [2, 1, 0, 3],
    PixelFormat.Argb32 => [1, 2, 3, 0],
    _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not a layout a surface is written back as."),
  };

  private static byte _ToByte(float value)
    => value <= 0f ? (byte)0 : value >= 1f ? (byte)255 : (byte)(value * 255f + 0.5f);

  private static int _ToWord(float value)
    => value <= 0f ? 0 : value >= 1f ? 65535 : (int)(value * 65535f + 0.5f);

  private static void _RequireSamples(int available, int needed, PixelFormat format) {
    if (available < needed)
      throw new ArgumentException(
        $"A {format} picture of this size needs {needed} byte(s) of pixel data but carries {available}.",
        "image");
  }
}
