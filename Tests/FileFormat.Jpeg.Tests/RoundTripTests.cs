using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using FileFormat.Jpeg;
using NUnit.Framework;

namespace FileFormat.Jpeg.Tests;

[TestFixture]
public sealed class RoundTripTests {

  private static byte[] _CreateRgbPixelData(int width, int height) {
    var data = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var i = (y * width + x) * 3;
      data[i] = (byte)(x * 30 % 256);
      data[i + 1] = (byte)(y * 30 % 256);
      data[i + 2] = 128;
    }

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
  [Category("Integration")]
  public void RoundTrip_LossyEncode_DimensionsPreserved() {
    const int width = 16;
    const int height = 16;
    var pixels = _CreateRgbPixelData(width, height);

    var encoded = JpegWriter.LossyEncode(pixels, width, height, 90, JpegMode.Baseline, JpegSubsampling.Chroma444, true, false);
    var decoded = JpegReader.FromBytes(encoded);

    Assert.That(decoded.Width, Is.EqualTo(width));
    Assert.That(decoded.Height, Is.EqualTo(height));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale_DetectedCorrectly() {
    const int width = 8;
    const int height = 8;
    var pixels = new byte[width * height];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 4 % 256);

    var encoded = JpegWriter.LossyEncode(pixels, width, height, 90, JpegMode.Baseline, JpegSubsampling.Chroma444, true, true);
    var decoded = JpegReader.FromBytes(encoded);

    Assert.That(decoded.IsGrayscale, Is.True);
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LosslessTranscode_DimensionsPreserved() {
    const int width = 8;
    const int height = 8;
    var jpegBytes = _CreateTestJpegBytes(width, height);

    var transcoded = JpegWriter.LosslessTranscode(jpegBytes, JpegMode.Progressive, true, false);
    var decoded = JpegReader.FromBytes(transcoded);

    Assert.That(decoded.Width, Is.EqualTo(width));
    Assert.That(decoded.Height, Is.EqualTo(height));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LosslessTranscode_PreservesRawBytes() {
    var jpegBytes = _CreateTestJpegBytes();

    var transcoded = JpegWriter.LosslessTranscode(jpegBytes, JpegMode.Baseline, true, false);
    var decoded = JpegReader.FromBytes(transcoded);

    Assert.That(decoded.RawJpegBytes, Is.Not.Null);
    Assert.That(decoded.RawJpegBytes!.Length, Is.GreaterThan(0));
  }
}
