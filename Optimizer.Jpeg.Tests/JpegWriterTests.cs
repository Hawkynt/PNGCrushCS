using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using FileFormat.Jpeg;
using NUnit.Framework;

namespace Optimizer.Jpeg.Tests;

[TestFixture]
public sealed class JpegWriterTests {
  private static byte[] _CreateTestJpeg(int width = 16, int height = 16, int quality = 90) {
    using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      bmp.SetPixel(x, y, Color.FromArgb(255, x * 16 % 256, y * 16 % 256, (x + y) * 8 % 256));

    using var ms = new MemoryStream();
    var encoder = ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);
    var encoderParams = new EncoderParameters(1) {
      Param = { [0] = new EncoderParameter(Encoder.Quality, (long)quality) }
    };
    bmp.Save(ms, encoder, encoderParams);
    return ms.ToArray();
  }

  [Test]
  [Category("Unit")]
  public void LossyEncode_Rgb_ProducesValidJpeg() {
    var rgb = new byte[8 * 8 * 3];
    for (var i = 0; i < rgb.Length; ++i)
      rgb[i] = (byte)(i % 256);

    var result = JpegWriter.LossyEncode(rgb, 8, 8, 90, JpegMode.Baseline,
      JpegSubsampling.Chroma444, true, false);

    Assert.That(result.Length, Is.GreaterThan(0));
    // JPEG starts with FFD8
    Assert.That(result[0], Is.EqualTo(0xFF));
    Assert.That(result[1], Is.EqualTo(0xD8));
  }

  [Test]
  [Category("Unit")]
  public void LossyEncode_Grayscale_ProducesValidJpeg() {
    var gray = new byte[8 * 8];
    for (var i = 0; i < gray.Length; ++i)
      gray[i] = (byte)(i * 4 % 256);

    var result = JpegWriter.LossyEncode(gray, 8, 8, 85, JpegMode.Baseline,
      JpegSubsampling.Chroma444, true, true);

    Assert.That(result.Length, Is.GreaterThan(0));
    Assert.That(result[0], Is.EqualTo(0xFF));
    Assert.That(result[1], Is.EqualTo(0xD8));
  }

  [Test]
  [Category("Unit")]
  public void LossyEncode_Progressive_ProducesOutput() {
    var rgb = new byte[16 * 16 * 3];
    for (var i = 0; i < rgb.Length; ++i)
      rgb[i] = (byte)(i % 256);

    var result = JpegWriter.LossyEncode(rgb, 16, 16, 80, JpegMode.Progressive,
      JpegSubsampling.Chroma420, true, false);

    Assert.That(result.Length, Is.GreaterThan(0));
  }

  [Test]
  [Category("Integration")]
  public void LosslessTranscode_BaselineToProgressive_ProducesValidJpeg() {
    var inputJpeg = _CreateTestJpeg();

    var result = JpegWriter.LosslessTranscode(inputJpeg, JpegMode.Progressive, true, false);

    Assert.That(result.Length, Is.GreaterThan(0));
    Assert.That(result[0], Is.EqualTo(0xFF));
    Assert.That(result[1], Is.EqualTo(0xD8));
  }

  [Test]
  [Category("Integration")]
  public void LosslessTranscode_OptimizeHuffman_SmallerOrEqual() {
    var inputJpeg = _CreateTestJpeg(32, 32);

    var unoptimized = JpegWriter.LosslessTranscode(inputJpeg, JpegMode.Baseline, false, false);
    var optimized = JpegWriter.LosslessTranscode(inputJpeg, JpegMode.Baseline, true, false);

    Assert.That(optimized.Length, Is.LessThanOrEqualTo(unoptimized.Length),
      "Huffman-optimized should be smaller or equal");
  }
}
