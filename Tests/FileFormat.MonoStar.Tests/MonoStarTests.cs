using System;
using System.IO;
using FileFormat.Core;
using FileFormat.MonoStar;

namespace FileFormat.MonoStar.Tests;

[TestFixture]
public sealed class MonoStarTests {

  private static RawImage _Checkerboard(int width, int height) {
    var data = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var o = (y * width + x) * 4;
      var ink = (x + y) % 2 == 0;
      data[o] = data[o + 1] = data[o + 2] = (byte)(ink ? 0 : 255);
      data[o + 3] = 255;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Bgra32, PixelData = data };
  }

  [TestCase(1, 2)]
  [TestCase(8, 2)]
  [TestCase(9, 2)]
  [TestCase(16, 2)]
  [TestCase(17, 4)]
  [Category("Unit")]
  public void StrideFor_PadsRowsToWholeWords(int width, int expected)
    => Assert.That(MonoStarFile.StrideFor(width), Is.EqualTo(expected));

  [Test]
  [Category("Unit")]
  public void ToBytes_StoresBothDimensionsOneLessThanTheyAre() {
    var bytes = MonoStarWriter.ToBytes(MonoStarFile.FromRawImage(_Checkerboard(64, 48)));

    Assert.Multiple(() => {
      Assert.That((bytes[0] << 8) + bytes[1], Is.EqualTo(63));
      Assert.That((bytes[2] << 8) + bytes[3], Is.EqualTo(47));
      Assert.That(bytes[4], Is.Zero);
      Assert.That(bytes[5], Is.EqualTo(1));
    });
  }

  [TestCase(64, 48)]
  [TestCase(17, 9)]
  [Category("Unit")]
  public void RoundTrip_PreservesTheSizeAndBitmap(int width, int height) {
    var file = MonoStarFile.FromRawImage(_Checkerboard(width, height));
    var restored = MonoStarReader.FromBytes(MonoStarWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(width));
      Assert.That(restored.Height, Is.EqualTo(height));
      Assert.That(restored.BitmapData, Is.EqualTo(file.BitmapData));
    });
  }

  [TestCase(64, 48)]
  [TestCase(17, 9)]
  [Category("Unit")]
  public void EncodeThenDecode_ReproducesEveryPixel(int width, int height) {
    // Row padding means a width that is not a multiple of sixteen has bits nobody draws; getting
    // the stride wrong shears the picture rather than losing a pixel, so this checks all of them.
    var source = _Checkerboard(width, height);
    var decoded = MonoStarFile.ToRawImage(MonoStarFile.FromRawImage(source));

    for (var i = 0; i < width * height; ++i) {
      var expected = source.PixelData[i * 4] < 128 ? 1 : 0;
      Assert.That(decoded.PixelData[i], Is.EqualTo(expected), $"pixel {i}");
    }
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_DrawsInkOnPaper() {
    var raw = MonoStarFile.ToRawImage(MonoStarFile.FromRawImage(_Checkerboard(16, 2)));

    Assert.Multiple(() => {
      Assert.That(raw.PaletteCount, Is.EqualTo(2));
      Assert.That(raw.Palette![..3], Is.EqualTo(new byte[] { 255, 255, 255 }));
      Assert.That(raw.Palette[3..6], Is.EqualTo(new byte[] { 0, 0, 0 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAColorStarObject() {
    // ColorSTar shares the extension but leads with ASCII palette entries, so the marker differs.
    var bytes = MonoStarWriter.ToBytes(MonoStarFile.FromRawImage(_Checkerboard(16, 2)));
    bytes[5] = 4;

    Assert.Throws<InvalidDataException>(() => MonoStarReader.FromBytes(bytes));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsASizeThatDoesNotMatchTheData() {
    var bytes = MonoStarWriter.ToBytes(MonoStarFile.FromRawImage(_Checkerboard(16, 2)));
    bytes[3] = 9;

    Assert.Throws<InvalidDataException>(() => MonoStarReader.FromBytes(bytes));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_RejectsAnEmptyImage() {
    var raw = new RawImage { Width = 0, Height = 0, Format = PixelFormat.Bgra32, PixelData = [] };

    Assert.Throws<ArgumentException>(() => MonoStarFile.FromRawImage(raw));
  }
}
