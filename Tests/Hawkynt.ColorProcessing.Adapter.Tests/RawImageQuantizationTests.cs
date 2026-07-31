using System;
using System.Collections.Generic;
using System.Linq;
using FileFormat.Core;
using Hawkynt.ColorProcessing.Adapter;
using Hawkynt.ColorProcessing.Dithering;

namespace Hawkynt.ColorProcessing.Adapter.Tests;

/// <summary>
/// The colour library's ditherers, driven over a <see cref="RawImage"/> on whatever this is
/// running on.
/// </summary>
/// <remarks>
/// The point of these is not that dithering is correct — that is the library's business and it has
/// its own tests. The point is that it runs at all off Windows, which is what the adapter exists
/// to prove, and that the pieces line up: the palette goes in the way the ditherer expects and the
/// indices come back meaning what they should.
/// </remarks>
[TestFixture]
public sealed class RawImageQuantizationTests {

  /// <summary>A picture already made of the palette's colours must come back untouched.</summary>
  /// <remarks>
  /// There is nothing for a ditherer to spread here, so every one of them has to agree — which
  /// makes this the check that catches a palette handed over in the wrong channel order, the
  /// mistake this kind of adapter exists to make.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void PictureAlreadyInThePalette_ComesBackUnchanged() {
    byte[] palette = [0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255, 255, 255, 0];
    var source = _Sample(palette, 32, 16);

    var reduced = RawImageQuantization.Dither(source, palette, default(NoDithering));
    var actual = PixelConverter.Convert(reduced, PixelFormat.Rgb24);

    for (var i = 0; i < source.PixelData.Length; ++i) {
      if (source.PixelData[i] == actual.PixelData[i])
        continue;

      Assert.Fail($"byte {i} went in as {source.PixelData[i]} and came back as {actual.PixelData[i]}");
    }
  }

  /// <summary>A gradient through a two-colour palette, dithered several ways.</summary>
  /// <remarks>
  /// A ditherer with settings — a diffusion kernel, an ordered matrix — is not usable as
  /// <c>default</c>: an empty kernel indexes past its own array. The named presets are the
  /// configured instances, and they are what a caller should reach for.
  /// </remarks>
  [TestCaseSource(nameof(Ditherers))]
  [Category("Unit")]
  public void Ditherer_StaysWithinThePaletteAndUsesIt(string name, Func<RawImage, byte[], RawImage> dither) {
    byte[] palette = [0, 0, 0, 255, 255, 255];
    var source = _Gradient(64, 32);

    var reduced = dither(source, palette);

    Assert.Multiple(() => {
      Assert.That(reduced.Width, Is.EqualTo(64), name);
      Assert.That(reduced.Height, Is.EqualTo(32), name);
      Assert.That(reduced.PixelData.All(i => i < 2), Is.True, $"{name}: used an index past the palette");

      // A gradient through a two-colour palette has to use both, or nothing happened at all.
      Assert.That(reduced.PixelData.Distinct().Count(), Is.EqualTo(2), $"{name}: a gradient came out flat");
    });
  }

  private static IEnumerable<TestCaseData> Ditherers() {
    yield return new TestCaseData(
      "ErrorDiffusion.FloydSteinberg",
      (Func<RawImage, byte[], RawImage>)((i, p) => RawImageQuantization.Dither(i, p, ErrorDiffusion.FloydSteinberg)));
    yield return new TestCaseData(
      "ErrorDiffusion.Atkinson",
      (Func<RawImage, byte[], RawImage>)((i, p) => RawImageQuantization.Dither(i, p, ErrorDiffusion.Atkinson)));
    yield return new TestCaseData(
      "OrderedDitherer.Bayer4x4",
      (Func<RawImage, byte[], RawImage>)((i, p) => RawImageQuantization.Dither(i, p, OrderedDitherer.Bayer4x4)));
    yield return new TestCaseData(
      "OrderedDitherer.Halftone8x8",
      (Func<RawImage, byte[], RawImage>)((i, p) => RawImageQuantization.Dither(i, p, OrderedDitherer.Halftone8x8)));
  }

  private static RawImage _Sample(byte[] palette, int width, int height) {
    var rgb = new byte[width * height * 3];
    var colors = palette.Length / 3;

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      var entry = ((x / 4 + y / 3) % colors) * 3;
      rgb[at] = palette[entry];
      rgb[at + 1] = palette[entry + 1];
      rgb[at + 2] = palette[entry + 2];
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private static RawImage _Gradient(int width, int height) {
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      rgb[at] = rgb[at + 1] = rgb[at + 2] = (byte)(x * 255 / (width - 1));
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }
}
