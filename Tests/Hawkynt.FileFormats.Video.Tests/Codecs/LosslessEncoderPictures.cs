using System;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Video.Tests.Codecs;

/// <summary>
/// Pictures for driving a lossless encoder down every path it has: noise a key frame has to state in
/// full, a patch that changes some blocks and leaves the rest, a shift that a motion search can find,
/// and the exact comparison that says the decoder gave back what the encoder was given.
/// </summary>
internal static class LosslessEncoderPictures {

  /// <summary>Noise in <paramref name="format"/>, with a full 256-entry palette where the format is indexed.</summary>
  public static RawImage Noise(int width, int height, PixelFormat format, int seed) {
    var random = new Random(seed);
    var pixels = new byte[width * height * RawImage.BytesPerPixel(format)];
    random.NextBytes(pixels);

    byte[]? palette = null;
    if (format == PixelFormat.Indexed8) {
      palette = new byte[256 * 3];
      random.NextBytes(palette);
    }

    return new() {
      Width = width,
      Height = height,
      Format = format,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = palette == null ? 0 : 256,
    };
  }

  /// <summary>A copy of <paramref name="source"/> with one rectangle re-randomised and everything else untouched.</summary>
  public static RawImage Patched(RawImage source, int x, int y, int width, int height, int seed) {
    var random = new Random(seed);
    var bpp = RawImage.BytesPerPixel(source.Format);
    var pixels = (byte[])source.PixelData.Clone();
    var x1 = Math.Min(source.Width, x + width);
    var y1 = Math.Min(source.Height, y + height);
    for (var row = y; row < y1; ++row)
      random.NextBytes(pixels.AsSpan((row * source.Width + x) * bpp, (x1 - x) * bpp));

    return With(source, pixels);
  }

  /// <summary>
  /// <paramref name="source"/> moved by (<paramref name="dx"/>, <paramref name="dy"/>) — positive
  /// is right and down — with the strip it uncovers filled with noise, so a block that moved can be
  /// copied from where it was and only the strip has to be sent outright.
  /// </summary>
  public static RawImage Shifted(RawImage source, int dx, int dy, int seed) {
    var random = new Random(seed);
    var bpp = RawImage.BytesPerPixel(source.Format);
    var pixels = new byte[source.Width * source.Height * bpp];
    random.NextBytes(pixels);
    for (var y = 0; y < source.Height; ++y) {
      var sourceY = y - dy;
      if (sourceY < 0 || sourceY >= source.Height)
        continue;

      for (var x = 0; x < source.Width; ++x) {
        var sourceX = x - dx;
        if (sourceX < 0 || sourceX >= source.Width)
          continue;

        Array.Copy(source.PixelData, (sourceY * source.Width + sourceX) * bpp, pixels, (y * source.Width + x) * bpp, bpp);
      }
    }

    return With(source, pixels);
  }

  /// <summary>A copy of <paramref name="source"/> whose palette is different and whose indices are not.</summary>
  public static RawImage Repainted(RawImage source, int seed) {
    var random = new Random(seed);
    var palette = new byte[source.Palette!.Length];
    random.NextBytes(palette);
    return With(source, palette: palette);
  }

  /// <summary>A copy of <paramref name="source"/> with the pixels or palette given swapped in and everything else kept.</summary>
  public static RawImage With(RawImage source, byte[]? pixels = null, byte[]? palette = null, bool dropPalette = false) => new() {
    Width = source.Width,
    Height = source.Height,
    Format = source.Format,
    PixelData = pixels ?? source.PixelData,
    Palette = dropPalette ? null : palette ?? source.Palette,
    PaletteCount = dropPalette ? 0 : source.PaletteCount,
  };

  /// <summary>
  /// The sequence every encoder here is driven with: noise, a patch, two shifts small enough for a
  /// motion search, a patched shift, noise again, and a frame identical to the one before it, then
  /// patches and shifts alternating up to <paramref name="count"/>.
  /// </summary>
  public static RawImage[] Sequence(int width, int height, PixelFormat format, int count, int seed) {
    var frames = new RawImage[count];
    var last = Noise(width, height, format, seed);
    for (var i = 0; i < count; ++i) {
      last = (i % 7) switch {
        0 => i == 0 ? last : Noise(width, height, format, seed + i),
        1 => Patched(last, width / 3, height / 3, Math.Max(1, width / 4), Math.Max(1, height / 4), seed + i),
        2 => Shifted(last, 3, -2, seed + i),
        3 => Shifted(last, -5, 4, seed + i),
        4 => Patched(Shifted(last, 1, 1, seed + i), 0, 0, Math.Max(1, width / 2), Math.Max(1, height / 5), seed - i),
        5 => Noise(width, height, format, seed + i),
        _ => last,
      };
      frames[i] = last;
    }

    return frames;
  }

  /// <summary>
  /// Asserts that <paramref name="actual"/> is exactly <paramref name="expected"/> once the expected
  /// picture is expressed in the decoder's own layout — the same conversion the encoder made on the
  /// way in, which loses nothing for the formats these encoders take.
  /// </summary>
  public static void AssertSame(RawImage expected, RawImage actual, string because) {
    Assert.That(actual.Width, Is.EqualTo(expected.Width), because);
    Assert.That(actual.Height, Is.EqualTo(expected.Height), because);

    if (actual.Format == PixelFormat.Indexed8) {
      Assert.That(expected.Format, Is.EqualTo(PixelFormat.Indexed8), because);
      Assert.That(actual.PixelData, Is.EqualTo(expected.PixelData), because);
      Assert.That(actual.Palette, Is.EqualTo(expected.Palette), because);
      return;
    }

    var reference = expected.Format == actual.Format ? expected : FastRawImageConverter.Convert(expected, actual.Format);
    var bytes = expected.Width * expected.Height * RawImage.BytesPerPixel(actual.Format);
    Assert.That(actual.PixelData.AsSpan(0, bytes).ToArray(), Is.EqualTo(reference.PixelData.AsSpan(0, bytes).ToArray()), because);
  }
}
