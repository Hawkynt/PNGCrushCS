using System;
using FileFormat.Core;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Crush.Viewer;

/// <summary>Moves pixels between the format registry's <see cref="RawImage"/> and the toolkit's images.</summary>
/// <remarks>
/// The toolkit hands a backend an <c>int[]</c> of 0xAARRGGBB values and gives back an opaque
/// <see cref="IImage"/> that only reports its size, so every conversion has to happen on this side.
/// <see cref="RawImage.ToBgra32"/> already normalises indexed, grayscale and high-depth sources, which
/// is why nothing here needs to know about pixel formats.
/// </remarks>
internal static class ImageBridge {

  /// <summary>Packs a picture into the toolkit's 0xAARRGGBB layout.</summary>
  internal static int[] ToArgb(RawImage source) {
    var bgra = source.ToBgra32();
    var count = source.Width * source.Height;
    var argb = new int[count];
    for (var i = 0; i < count; ++i) {
      var o = i * 4;
      argb[i] = bgra[o] | bgra[o + 1] << 8 | bgra[o + 2] << 16 | bgra[o + 3] << 24;
    }

    return argb;
  }

  /// <summary>Creates a toolkit image for a picture, or <c>null</c> when it has no pixels.</summary>
  internal static IImage? ToImage(IPlatformBackend backend, RawImage source)
    => source.Width < 1 || source.Height < 1 ? null : backend.CreateImage(source.Width, source.Height, ToArgb(source));

  /// <summary>
  /// Scales a picture into a square box without cropping it, centred on a transparent field.
  /// </summary>
  /// <remarks>
  /// This is a box filter over the source rectangle each destination pixel covers, not a sampler:
  /// a thumbnail is almost always a heavy reduction, and point sampling a 4000-pixel photograph down
  /// to 96 throws away so much that detailed images all collapse to the same noise.
  /// </remarks>
  internal static int[] ToThumbnailArgb(RawImage source, int edge) {
    var result = new int[edge * edge];
    if (source.Width < 1 || source.Height < 1)
      return result;

    var scale = Math.Min(edge / (double)source.Width, edge / (double)source.Height);
    var targetWidth = Math.Max(1, Math.Min(edge, (int)Math.Round(source.Width * scale)));
    var targetHeight = Math.Max(1, Math.Min(edge, (int)Math.Round(source.Height * scale)));
    var offsetX = (edge - targetWidth) / 2;
    var offsetY = (edge - targetHeight) / 2;

    var bgra = source.ToBgra32();
    var stride = source.Width * 4;

    for (var y = 0; y < targetHeight; ++y) {
      var sourceTop = y * source.Height / targetHeight;
      var sourceBottom = Math.Max(sourceTop + 1, (y + 1) * source.Height / targetHeight);
      for (var x = 0; x < targetWidth; ++x) {
        var sourceLeft = x * source.Width / targetWidth;
        var sourceRight = Math.Max(sourceLeft + 1, (x + 1) * source.Width / targetWidth);

        long b = 0, g = 0, r = 0, a = 0;
        var samples = 0;
        for (var sy = sourceTop; sy < sourceBottom; ++sy) {
          var row = sy * stride;
          for (var sx = sourceLeft; sx < sourceRight; ++sx) {
            var o = row + sx * 4;
            b += bgra[o];
            g += bgra[o + 1];
            r += bgra[o + 2];
            a += bgra[o + 3];
            ++samples;
          }
        }

        result[(offsetY + y) * edge + offsetX + x] =
          (int)(b / samples) | (int)(g / samples) << 8 | (int)(r / samples) << 16 | (int)(a / samples) << 24;
      }
    }

    return result;
  }
}
