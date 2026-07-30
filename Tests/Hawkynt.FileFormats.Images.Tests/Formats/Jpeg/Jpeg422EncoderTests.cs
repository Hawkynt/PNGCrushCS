using System.IO;

namespace FileFormat.Jpeg.Tests;

/// <summary>
/// Verifies the managed 4:2:2 JPEG encoder. JpegWriter.LossyEncode routes all modes
/// through JpegManagedEncoder. These tests confirm the resulting bytes are decodable JPEG
/// with SOF0 sampling factors that match the requested subsampling.
/// </summary>
[TestFixture]
public sealed class Jpeg422EncoderTests {

  private static byte[] _MakeRgbGradient(int width, int height) {
    var data = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var off = (y * width + x) * 3;
        data[off + 0] = (byte)(x * 255 / (width - 1));
        data[off + 1] = (byte)(y * 255 / (height - 1));
        data[off + 2] = (byte)((x + y) * 255 / (width + height - 2));
      }
    return data;
  }

  [Test]
  public void LossyEncode_Chroma422_ProducesValidJpeg() {
    var rgb = _MakeRgbGradient(32, 16);

    var bytes = JpegWriter.LossyEncode(
      rgb, 32, 16, quality: 90, JpegMode.Baseline,
      JpegSubsampling.Chroma422, optimizeHuffman: true, isGrayscale: false);

    Assert.That(bytes.Length, Is.GreaterThan(20),
      "Encoded JPEG should not be empty.");
    Assert.That(bytes[0], Is.EqualTo(0xFF));
    Assert.That(bytes[1], Is.EqualTo(0xD8), "JPEG must start with SOI (FF D8).");
    Assert.That(bytes[^2], Is.EqualTo(0xFF));
    Assert.That(bytes[^1], Is.EqualTo(0xD9), "JPEG must end with EOI (FF D9).");
  }

  [Test]
  public void LossyEncode_Chroma422_SamplingFactorsAre2x1And1x1() {
    var rgb = _MakeRgbGradient(32, 16);
    var bytes = JpegWriter.LossyEncode(
      rgb, 32, 16, quality: 90, JpegMode.Baseline,
      JpegSubsampling.Chroma422, optimizeHuffman: true, isGrayscale: false);

    var (yH, yV, cbH, cbV, crH, crV) = _ReadSof0SamplingFactors(bytes);
    Assert.Multiple(() => {
      Assert.That(yH, Is.EqualTo(2), "Y component H sampling factor for 4:2:2 should be 2.");
      Assert.That(yV, Is.EqualTo(1), "Y component V sampling factor for 4:2:2 should be 1.");
      Assert.That(cbH, Is.EqualTo(1), "Cb component H sampling factor for 4:2:2 should be 1.");
      Assert.That(cbV, Is.EqualTo(1), "Cb component V sampling factor for 4:2:2 should be 1.");
      Assert.That(crH, Is.EqualTo(1), "Cr component H sampling factor for 4:2:2 should be 1.");
      Assert.That(crV, Is.EqualTo(1), "Cr component V sampling factor for 4:2:2 should be 1.");
    });
  }

  [Test]
  public void LossyEncode_Chroma422_DecodesViaManagedDecoder() {
    var rgb = _MakeRgbGradient(32, 16);
    var bytes = JpegWriter.LossyEncode(
      rgb, 32, 16, quality: 90, JpegMode.Baseline,
      JpegSubsampling.Chroma422, optimizeHuffman: true, isGrayscale: false);

    var decoded = JpegReader.FromBytes(bytes);
    Assert.That(decoded.Width, Is.EqualTo(32));
    Assert.That(decoded.Height, Is.EqualTo(16));
    Assert.That(decoded.IsGrayscale, Is.False);
    Assert.That(decoded.RgbPixelData, Is.Not.Null);
    Assert.That(decoded.RgbPixelData!.Length, Is.EqualTo(32 * 16 * 3));
  }

  [Test]
  public void LossyEncode_Chroma422_NonMcuAlignedSize_RoundTrips() {
    // 17x9 -- tests edge replication for both axes (MCU is 16x8 in 4:2:2).
    var rgb = _MakeRgbGradient(17, 9);
    var bytes = JpegWriter.LossyEncode(
      rgb, 17, 9, quality: 75, JpegMode.Baseline,
      JpegSubsampling.Chroma422, optimizeHuffman: true, isGrayscale: false);

    var decoded = JpegReader.FromBytes(bytes);
    Assert.That(decoded.Width, Is.EqualTo(17));
    Assert.That(decoded.Height, Is.EqualTo(9));
  }

  [Test]
  public void LossyEncode_Chroma422_GrayscaleHandledByManagedEncoder() {
    // Grayscale doesn't subsample, so chroma mode is irrelevant.
    var gray = new byte[16 * 16];
    for (var i = 0; i < gray.Length; ++i) gray[i] = (byte)(i * 255 / (gray.Length - 1));

    var bytes = JpegWriter.LossyEncode(
      gray, 16, 16, quality: 90, JpegMode.Baseline,
      JpegSubsampling.Chroma422, optimizeHuffman: true, isGrayscale: true);

    Assert.That(bytes.Length, Is.GreaterThan(20));
    var decoded = JpegReader.FromBytes(bytes);
    Assert.That(decoded.IsGrayscale, Is.True, "Grayscale JPEG should have one component.");
  }

  /// <summary>Walks the JPEG byte stream to find SOF0 (FF C0) and reads the three components'
  /// (H&lt;&lt;4|V) sampling-factor bytes. Returns (yH, yV, cbH, cbV, crH, crV).</summary>
  private static (int yH, int yV, int cbH, int cbV, int crH, int crV) _ReadSof0SamplingFactors(byte[] data) {
    for (var i = 0; i < data.Length - 1; ++i) {
      if (data[i] != 0xFF || data[i + 1] != 0xC0) continue;
      // SOF0 layout (after FF C0): segLen[2] precision[1] height[2] width[2] nf[1]
      // then nf*3 bytes per component: id[1] (H<<4|V)[1] qtId[1]
      var componentsOffset = i + 2 + 2 + 1 + 2 + 2 + 1;
      var yHv = data[componentsOffset + 1];
      var cbHv = data[componentsOffset + 4];
      var crHv = data[componentsOffset + 7];
      return (yHv >> 4, yHv & 0x0F, cbHv >> 4, cbHv & 0x0F, crHv >> 4, crHv & 0x0F);
    }
    Assert.Fail("SOF0 marker not found in encoded JPEG.");
    return default;
  }
}
