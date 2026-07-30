using System;
using System.IO;
using FileFormat.Core;
using FileFormat.ZxRgb3;

namespace FileFormat.ZxRgb3.Tests;

[TestFixture]
public sealed class ZxRgb3Tests {

  /// <summary>All eight reachable colours, one per vertical band.</summary>
  private static RawImage _Bands() {
    const int width = ZxSpectrumGraphics.ScreenWidth;
    const int height = ZxSpectrumGraphics.ScreenHeight;
    var data = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var o = (y * width + x) * 4;
      var band = x / (width / ZxRgb3File.ColorCount);
      data[o + 2] = (byte)((band & 1) != 0 ? 255 : 0);
      data[o + 1] = (byte)((band & 2) != 0 ? 255 : 0);
      data[o] = (byte)((band & 4) != 0 ? 255 : 0);
      data[o + 3] = 255;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Bgra32, PixelData = data };
  }

  [Test]
  [Category("Unit")]
  public void FileSize_IsThreeDisplayFiles()
    => Assert.That(ZxRgb3File.FileSize, Is.EqualTo(18432));

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesTheBitmaps() {
    var file = ZxRgb3File.FromRawImage(_Bands());

    Assert.That(ZxRgb3Reader.FromBytes(ZxRgb3Writer.ToBytes(file)).BitmapData, Is.EqualTo(file.BitmapData));
  }

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReproducesAllEightColoursExactly() {
    // Each component is a single bit, so an eight-colour picture goes in without loss. A wrong
    // component order or scanline interleave would show up immediately here.
    var source = _Bands();
    var decoded = ZxRgb3File.ToRawImage(ZxRgb3File.FromRawImage(source));

    for (var i = 0; i < ZxSpectrumGraphics.ScreenWidth * ZxSpectrumGraphics.ScreenHeight; ++i) {
      Assert.That(decoded.PixelData[i * 3], Is.EqualTo(source.PixelData[i * 4 + 2]), $"pixel {i} red");
      Assert.That(decoded.PixelData[i * 3 + 1], Is.EqualTo(source.PixelData[i * 4 + 1]), $"pixel {i} green");
      Assert.That(decoded.PixelData[i * 3 + 2], Is.EqualTo(source.PixelData[i * 4]), $"pixel {i} blue");
    }
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_UsesTheSpectrumScanlineOrder() {
    // Scanline 1 lives a third of the way down the display file, not one row after scanline 0 —
    // reading it linearly shears the picture into thirds.
    Assert.That(ZxSpectrumGraphics.LineOffset(1), Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAWronglySizedFile()
    => Assert.Throws<InvalidDataException>(() => ZxRgb3Reader.FromBytes(new byte[6144]));

  [Test]
  [Category("Unit")]
  public void FromRawImage_RejectsOtherSizes() {
    var raw = new RawImage { Width = 256, Height = 200, Format = PixelFormat.Bgra32, PixelData = new byte[256 * 200 * 4] };

    Assert.Throws<ArgumentException>(() => ZxRgb3File.FromRawImage(raw));
  }
}
