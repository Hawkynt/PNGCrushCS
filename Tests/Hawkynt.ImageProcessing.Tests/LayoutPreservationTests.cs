using System;
using FileFormat.Core;

namespace Hawkynt.ImageProcessing.Tests;

/// <summary>
/// What a picture carries — its layout, its precision, its palette, its alpha — survives an
/// operation that has no reason to change it.
/// </summary>
[TestFixture]
[Category("Unit")]
public sealed class LayoutPreservationTests {

  [TestCase(PixelFormat.Gray8)]
  [TestCase(PixelFormat.GrayAlpha16)]
  [TestCase(PixelFormat.Rgb24)]
  [TestCase(PixelFormat.Bgr24)]
  [TestCase(PixelFormat.Rgba32)]
  [TestCase(PixelFormat.Bgra32)]
  [TestCase(PixelFormat.Argb32)]
  [TestCase(PixelFormat.Gray16)]
  [TestCase(PixelFormat.GrayAlpha32)]
  [TestCase(PixelFormat.Rgb48)]
  [TestCase(PixelFormat.Rgba64)]
  public void IdentityCurve_ReturnsTheSameBytesInTheSameLayout(PixelFormat format) {
    var source = _Noise(format, 13, 7);

    var result = ColorCorrection.ApplyCurve(source, static value => value);

    Assert.That(result.Format, Is.EqualTo(format));
    Assert.That(result.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  public void SixteenBitPicture_KeepsSixteenBitPrecision() {
    // Values one 16-bit step apart, which an 8-bit round trip would merge.
    var pixels = new byte[4 * 2];
    ushort[] words = [30000, 30001, 30002, 30003];
    for (var i = 0; i < words.Length; ++i) {
      pixels[i * 2] = (byte)(words[i] >> 8);
      pixels[i * 2 + 1] = (byte)words[i];
    }

    var source = new RawImage { Width = 4, Height = 1, Format = PixelFormat.Gray16, PixelData = pixels };
    var result = ColorCorrection.Gamma(source, 1.0 + 1e-9);

    Assert.That(result.Format, Is.EqualTo(PixelFormat.Gray16));
    var distinct = new System.Collections.Generic.HashSet<int>();
    for (var i = 0; i < 4; ++i)
      distinct.Add((result.PixelData[i * 2] << 8) | result.PixelData[i * 2 + 1]);

    Assert.That(distinct, Has.Count.EqualTo(4), "four neighbouring 16-bit levels were merged");
  }

  [Test]
  public void Correction_LeavesAlphaAlone() {
    var source = _Noise(PixelFormat.Rgba32, 9, 9);

    var result = ColorCorrection.BrightnessContrast(source, 40, 30);

    for (var i = 3; i < source.PixelData.Length; i += 4)
      Assert.That(result.PixelData[i], Is.EqualTo(source.PixelData[i]), $"alpha of pixel {i / 4}");
  }

  [Test]
  public void Correction_OnIndexedPicture_ChangesOnlyThePalette() {
    var source = _Indexed();

    var result = ColorCorrection.Gamma(source, 2.0);

    Assert.Multiple(() => {
      Assert.That(result.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(result.PixelData, Is.EqualTo(source.PixelData));
      Assert.That(result.PaletteCount, Is.EqualTo(source.PaletteCount));
      Assert.That(result.AlphaTable, Is.EqualTo(source.AlphaTable));
      Assert.That(result.Palette![3], Is.EqualTo((byte)Math.Round(255 * Math.Sqrt(64 / 255.0))));
    });
  }

  [Test]
  public void AutoLevels_OnIndexedPicture_IgnoresPaletteEntriesNoPixelUses() {
    // Entry 0 is black but unused; the pixels span 64..192 only.
    var source = _Indexed();

    var result = ColorCorrection.AutoLevels(source);

    Assert.That(result.Format, Is.EqualTo(PixelFormat.Indexed8));
    Assert.That(result.Palette![3], Is.EqualTo(0), "the darkest used entry becomes black");
    Assert.That(result.Palette![6], Is.EqualTo(255), "the lightest used entry becomes white");
  }

  [TestCase(PixelFormat.Indexed1, 1)]
  [TestCase(PixelFormat.Indexed4, 4)]
  [TestCase(PixelFormat.Indexed8, 8)]
  public void NearestResize_OnIndexedPicture_StaysIndexedAndCopiesIndices(PixelFormat format, int bits) {
    const int width = 5, height = 3;
    var mask = (1 << bits) - 1;
    var indices = new int[width * height];
    for (var i = 0; i < indices.Length; ++i)
      indices[i] = (i * 7 + 3) & mask;

    var source = new RawImage {
      Width = width, Height = height, Format = format, PixelData = _Pack(indices, bits),
      Palette = new byte[(mask + 1) * 3], PaletteCount = mask + 1,
    };

    var result = ImageScaler.Resize(source, width * 2, height * 2, ResampleKernel.Nearest);

    Assert.That(result.Format, Is.EqualTo(format));
    Assert.That(result.Palette, Is.SameAs(source.Palette));
    for (var y = 0; y < height * 2; ++y)
      for (var x = 0; x < width * 2; ++x)
        Assert.That(_Unpack(result.PixelData, y * width * 2 + x, bits), Is.EqualTo(indices[y / 2 * width + x / 2]), $"({x},{y})");
  }

  [Test]
  public void SmoothResize_OnIndexedPicture_ComesBackAsColour() {
    var result = ImageScaler.Resize(_Indexed(), 8, 8, ResampleKernel.Lanczos);

    Assert.That(result.IsIndexed, Is.False);
    Assert.That(result.HasAlpha, Is.True, "the palette's transparency survives as an alpha channel");
  }

  [Test]
  public void UnrecognisedLayout_ComesBackAsPlainRgbWithoutInventingAlpha() {
    var source = new RawImage { Width = 2, Height = 1, Format = PixelFormat.Rgb565, PixelData = [0x1F, 0xF8, 0xE0, 0x07] };

    var result = ImageEffects.Blur(source, 1, 0.5);

    Assert.That(result.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  private static RawImage _Noise(PixelFormat format, int width, int height) {
    var bytes = new byte[width * height * RawImage.BytesPerPixel(format)];
    new Random(7).NextBytes(bytes);
    return new() { Width = width, Height = height, Format = format, PixelData = bytes };
  }

  private static RawImage _Indexed() => new() {
    Width = 4,
    Height = 2,
    Format = PixelFormat.Indexed8,
    PixelData = [1, 2, 1, 2, 2, 1, 1, 1],
    Palette = [0, 0, 0, 64, 64, 64, 192, 192, 192],
    PaletteCount = 3,
    AlphaTable = [255, 128, 255],
  };

  private static byte[] _Pack(int[] indices, int bits) {
    var data = new byte[(indices.Length * bits + 7) / 8];
    for (var i = 0; i < indices.Length; ++i) {
      var bit = i * bits;
      data[bit / 8] |= (byte)(indices[i] << (8 - bits - bit % 8));
    }

    return data;
  }

  private static int _Unpack(byte[] data, int pixel, int bits) {
    var bit = pixel * bits;
    return (data[bit / 8] >> (8 - bits - bit % 8)) & ((1 << bits) - 1);
  }
}
