using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using FileFormat.Jpeg;
using NUnit.Framework;

namespace FileFormat.Jpeg.Tests;

[TestFixture]
public sealed class JpegWriterTests {

  private static byte[] _CreateRgbPixelData(int width, int height) {
    var data = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var i = (y * width + x) * 3;
      data[i] = (byte)(x * 60 % 256);
      data[i + 1] = (byte)(y * 60 % 256);
      data[i + 2] = 128;
    }

    return data;
  }

  private static byte[] _CreateGrayscalePixelData(int width, int height) {
    var data = new byte[width * height];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      data[y * width + x] = (byte)((x + y) * 16 % 256);

    return data;
  }

  private static byte[] _CreateTestJpegBytes(int width = 8, int height = 8) {
    using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      bmp.SetPixel(x, y, Color.FromArgb(255, x * 30, y * 30, 128));

    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Jpeg);
    return ms.ToArray();
  }

  [Test]
  [Category("Unit")]
  public void LossyEncode_Rgb_ProducesValidJpeg() {
    const int width = 8;
    const int height = 8;
    var pixels = _CreateRgbPixelData(width, height);

    var result = JpegWriter.LossyEncode(pixels, width, height, 85, JpegMode.Baseline, JpegSubsampling.Chroma444, true, false);

    Assert.That(result.Length, Is.GreaterThan(2));
    Assert.That(result[0], Is.EqualTo(0xFF));
    Assert.That(result[1], Is.EqualTo(0xD8));
  }

  [Test]
  [Category("Unit")]
  public void LossyEncode_Grayscale_ProducesValidJpeg() {
    const int width = 8;
    const int height = 8;
    var pixels = _CreateGrayscalePixelData(width, height);

    var result = JpegWriter.LossyEncode(pixels, width, height, 85, JpegMode.Baseline, JpegSubsampling.Chroma444, true, true);

    Assert.That(result.Length, Is.GreaterThan(2));
    Assert.That(result[0], Is.EqualTo(0xFF));
    Assert.That(result[1], Is.EqualTo(0xD8));
  }

  [Test]
  [Category("Unit")]
  public void LossyEncode_Progressive_ProducesOutput() {
    const int width = 8;
    const int height = 8;
    var pixels = _CreateRgbPixelData(width, height);

    var result = JpegWriter.LossyEncode(pixels, width, height, 85, JpegMode.Progressive, JpegSubsampling.Chroma444, true, false);

    Assert.That(result, Is.Not.Empty);
  }

  [Test]
  [Category("Unit")]
  public void LosslessTranscode_BaselineToProgressive_ProducesOutput() {
    var jpegBytes = _CreateTestJpegBytes();

    var result = JpegWriter.LosslessTranscode(jpegBytes, JpegMode.Progressive, true, false);

    Assert.That(result.Length, Is.GreaterThan(2));
    Assert.That(result[0], Is.EqualTo(0xFF));
    Assert.That(result[1], Is.EqualTo(0xD8));
  }
}
