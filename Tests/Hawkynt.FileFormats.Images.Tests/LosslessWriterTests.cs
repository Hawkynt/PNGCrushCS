using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>
/// Some vintage formats can hold everything a picture offers and some cannot. These are the ones
/// that can — either because they carry whole pixels, or because the picture given to them is
/// already within what they hold — and for those, writing and reading back has to return exactly
/// what went in.
/// </summary>
/// <remarks>
/// A weaker check would be that a second round trip agrees with the first, which any writer passes
/// as soon as it is self-consistent, however wrong. Comparing against the input instead is the only
/// version that catches a writer and a reader making the same mistake — which is the failure this
/// project has hit more than once.
/// </remarks>
[TestFixture]
public sealed class LosslessWriterTests {

  /// <summary>A format that loses nothing, and the pictures it is expected to hold exactly.</summary>
  public sealed record Case(string Name, ImageFormat Format, bool Monochrome, int Width, int Height);

  private static readonly Case[] _Cases = [
    new("HP 48 graphics object", ImageFormat.Hp48Grob, true, 131, 37),
    new("HP 48, whole bytes across", ImageFormat.Hp48Grob, true, 64, 12),
    new("DEGAS Elite icon", ImageFormat.DegasIcon, true, 37, 23),
    new("DEGAS Elite icon, whole words", ImageFormat.DegasIcon, true, 32, 8),
    new("Printfox block", ImageFormat.Printfox, true, 88, 40),
    new("True-colour GEM image", ImageFormat.TrueColorImg, false, 96, 40),
    new("True-colour GEM image, one row", ImageFormat.TrueColorImg, false, 300, 1),
  ];

  private static IEnumerable<TestCaseData> Cases() {
    foreach (var one in _Cases)
      yield return new TestCaseData(one).SetName($"{{m}}({one.Name})");
  }

  [TestCaseSource(nameof(Cases))]
  [Category("Unit")]
  public void Written_ReadsBackUnchanged(Case one) {
    var source = _Sample(one.Monochrome, one.Width, one.Height);

    var bytes = FormatRegistry.Write(source, one.Format);
    Assert.That(bytes, Is.Not.Null.And.Not.Empty, $"{one.Name}: produced no bytes");

    // The format is named rather than detected: these carry no signature a sniffer could use, and
    // what is under test is the encoding, not the detection.
    var entry = FormatRegistry.GetEntry(one.Format);
    Assert.That(entry, Is.Not.Null, $"{one.Name}: not registered");

    var read = entry!.LoadRawImageFromBytes(bytes!);
    Assert.That(read, Is.Not.Null, $"{one.Name}: our own output did not read back");

    var actual = PixelConverter.Convert(read!, PixelFormat.Rgb24);
    Assert.Multiple(() => {
      Assert.That(actual.Width, Is.GreaterThanOrEqualTo(one.Width), $"{one.Name}: too narrow");
      Assert.That(actual.Height, Is.GreaterThanOrEqualTo(one.Height), $"{one.Name}: too short");
    });

    // A cell-based format rounds its size up, so only the area the picture covers is compared;
    // what lies outside it was never the picture's to describe.
    for (var y = 0; y < one.Height; ++y)
    for (var x = 0; x < one.Width; ++x) {
      var expected = (y * one.Width + x) * 3;
      var got = (y * actual.Width + x) * 3;

      if (source.PixelData[expected] == actual.PixelData[got]
          && source.PixelData[expected + 1] == actual.PixelData[got + 1]
          && source.PixelData[expected + 2] == actual.PixelData[got + 2])
        continue;

      Assert.Fail(
        $"{one.Name}: pixel {x},{y} went in as "
        + $"({source.PixelData[expected]},{source.PixelData[expected + 1]},{source.PixelData[expected + 2]}) "
        + $"and came back as ({actual.PixelData[got]},{actual.PixelData[got + 1]},{actual.PixelData[got + 2]})");
    }
  }

  /// <summary>
  /// A picture already within what the format holds: black and white for the one-bit formats, and
  /// full colour for the ones that carry whole pixels.
  /// </summary>
  private static RawImage _Sample(bool monochrome, int width, int height) {
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;

      if (monochrome) {
        // A pattern with runs in it and single pixels between them, so a run-length writer is
        // exercised on both and a stuck bit shows up as a shape rather than a shade.
        var ink = (x / 3 + y / 5) % 2 == 0 || (x % 7 == 3 && y % 4 != 1);
        rgb[at] = rgb[at + 1] = rgb[at + 2] = (byte)(ink ? 0 : 255);
        continue;
      }

      rgb[at] = (byte)(x * 37 + y);
      rgb[at + 1] = (byte)(y * 53 + x * 3);
      rgb[at + 2] = (byte)(x * y + 17);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }
}
