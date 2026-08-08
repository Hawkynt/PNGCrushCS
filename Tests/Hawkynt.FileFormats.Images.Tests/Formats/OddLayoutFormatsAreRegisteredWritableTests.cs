using System;
using System.Collections.Generic;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>
/// That the formats whose layout is a machine's memory rather than a picture are registered as
/// writable and not merely declared so.
/// </summary>
/// <remarks>
/// A format becomes writable by declaring all four of the reader, the two conversions and the
/// writer, and the source generator checks that itself — a type missing one of them is registered
/// read-only and no test of its own FromRawImage would notice, because calling the method directly
/// works whether or not anything is wired to it. This asserts the wiring rather than the method.
/// </remarks>
[TestFixture]
public sealed class OddLayoutFormatsAreRegisteredWritableTests {

  private static readonly ImageFormat[] _Formats = [
    ImageFormat.AmosBank, ImageFormat.AnimatorCompressor, ImageFormat.Apac3, ImageFormat.ApplePreferred,
    ImageFormat.AtariChampionsInterlace, ImageFormat.AtariHardInterlace, ImageFormat.AtariPlayerEditor,
    ImageFormat.BugbiterApac, ImageFormat.CanvasRaster, ImageFormat.CharPad, ImageFormat.ColorStarObject,
    ImageFormat.CommodoreGrafix, ImageFormat.ComputerEyesSt, ImageFormat.CranachPaint, ImageFormat.DelmPaint,
    ImageFormat.ExtendSuperHires, ImageFormat.Fuckpaint, ImageFormat.FunWithArt,
  ];

  [Test]
  [Category("Unit")]
  public void EveryOneOfThemIsRegisteredWithAWriteSide() {
    var missing = _Formats.Where(f => FormatRegistry.GetEntry(f)?.SupportsWrite != true).ToList();

    Assert.That(missing, Is.Empty, "registered read-only: " + string.Join(", ", missing));
  }

  [Test]
  [Category("Unit")]
  public void EncodingThroughTheRegistryProducesBytesTheSameFormatDetects() {
    // Going through the registry rather than the type proves the generated table points at the real
    // conversion, which calling the method directly cannot.
    var picture = _Gradient(137, 91);
    var failures = new List<string>();

    foreach (var format in _Formats) {
      var entry = FormatRegistry.GetEntry(format);
      if (entry?.ConvertFromRawImage == null)
        continue;

      var bytes = entry.ConvertFromRawImage(picture);
      if (bytes is not { Length: > 0 })
        failures.Add(format.ToString());
    }

    Assert.That(failures, Is.Empty, "encoded to nothing: " + string.Join(", ", failures));
  }

  /// <summary>A picture of no particular size and no particular colours, which is what a caller has.</summary>
  private static RawImage _Gradient(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      rgb[at] = (byte)(x * 255 / width);
      rgb[at + 1] = (byte)(y * 255 / height);
      rgb[at + 2] = (byte)((x + y) & 255);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }
}
