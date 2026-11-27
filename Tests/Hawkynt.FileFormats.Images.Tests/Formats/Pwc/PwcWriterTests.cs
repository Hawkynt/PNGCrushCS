using System;
using System.Collections.Generic;
using FileFormat.Core;
using FileFormat.Pwc;
using Hawkynt.FileFormats.Images;

namespace FileFormat.Pwc.Tests;

[TestFixture]
public sealed class PwcWriterTests {

  private static IEnumerable<string> FixtureNames() => PwcFixtures.Names();

  /// <summary>
  /// Handed pwcdecode's picture with the palette and options pwcencode chose, the writer must produce the
  /// very bytes pwcencode wrote: the models run in lock-step with the reference encoder, decision for decision.
  /// </summary>
  [TestCaseSource(nameof(FixtureNames))]
  [Category("Unit")]
  public void Writer_ReproducesPwcencodeByteForByte(string name) {
    var original = PwcFixtures.Pwc(name);
    var reference = PwcFixtures.Bmp(name);
    var file = PwcReader.FromBytes(original) with { PixelData = reference.Indices };

    Assert.That(PwcWriter.ToBytes(file), Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_PicksTheShallowestModel() {
    Assert.That(PwcFile.FromRawImage(_Indexed(5, 4, 2)).BitsPerPixel, Is.EqualTo(1));
    Assert.That(PwcFile.FromRawImage(_Indexed(5, 4, 9)).BitsPerPixel, Is.EqualTo(4));
    Assert.That(PwcFile.FromRawImage(_Indexed(5, 4, 17)).BitsPerPixel, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_UsesTheEncodersDefaultOptions() {
    Assert.That(PwcFile.FromRawImage(_Indexed(5, 4, 40)).CodingFlags, Is.EqualTo(PwcCodingFlags.NoListDecision));
    Assert.That(PwcFile.FromRawImage(_Indexed(5, 4, 256)).CodingFlags, Is.EqualTo(PwcCodingFlags.None));
    Assert.That(PwcFile.FromRawImage(_Indexed(5, 4, 40)).MaxLps, Is.EqualTo(PwcFile.DefaultMaxLps));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_TrueColourIsQuantised() {
    var pixels = new byte[7 * 5 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 37);

    var file = PwcFile.FromRawImage(new RawImage { Width = 7, Height = 5, Format = PixelFormat.Rgb24, PixelData = pixels });
    var back = PwcReader.FromBytes(PwcWriter.ToBytes(file));
    Assert.That(back.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void Write_ThroughTheRegistry_ReadsBack() {
    var raw = _Indexed(19, 11, 12);
    var bytes = FormatRegistry.Write(raw, ImageFormat.Pwc);
    Assert.That(bytes, Is.Not.Null);
    Assert.That(FormatRegistry.DetectFromBytes(bytes!), Is.EqualTo(ImageFormat.Pwc));
    Assert.That(PwcReader.FromBytes(bytes!).PixelData, Is.EqualTo(raw.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FourBitGrey_IsRefused() {
    var file = PwcFile.FromRawImage(_Indexed(5, 4, 9)) with { CodingFlags = PwcCodingFlags.Gray };
    Assert.Throws<ArgumentException>(() => PwcWriter.ToBytes(file));
  }

  [TestCase(0, 5)]
  [TestCase(5, 0)]
  [TestCase(65536, 1)]
  [Category("Unit")]
  public void ToBytes_DimensionsOutsideTheHeader_AreRefused(int width, int height) {
    var file = PwcFile.FromRawImage(_Indexed(5, 4, 9)) with { Width = width, Height = height };
    Assert.Throws<ArgumentException>(() => PwcWriter.ToBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_IndexBeyondThePalette_IsRefused() {
    var file = PwcFile.FromRawImage(_Indexed(5, 4, 9));
    var pixels = file.PixelData[..];
    pixels[3] = 12;
    Assert.Throws<ArgumentException>(() => PwcWriter.ToBytes(file with { PixelData = pixels }));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_UnsupportedDepth_IsRefused() {
    var file = PwcFile.FromRawImage(_Indexed(5, 4, 9)) with { BitsPerPixel = 2 };
    Assert.Throws<ArgumentException>(() => PwcWriter.ToBytes(file));
  }

  internal static RawImage _Indexed(int width, int height, int colors, int seed = 1) {
    var random = new Random(seed);
    var pixels = new byte[width * height];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      pixels[y * width + x] = (byte)(random.Next(4) == 0 ? random.Next(colors) : (x / 3 + y / 2) % colors);

    var palette = new byte[colors * 3];
    random.NextBytes(palette);
    return new() {
      Width = width, Height = height, Format = PixelFormat.Indexed8, PixelData = pixels, Palette = palette, PaletteCount = colors,
    };
  }
}
