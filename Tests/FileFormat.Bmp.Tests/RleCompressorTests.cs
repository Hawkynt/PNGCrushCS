using System;
using FileFormat.Bmp;

namespace FileFormat.Bmp.Tests;

[TestFixture]
public sealed class RleCompressorTests {

  [Test]
  [Category("Unit")]
  public void CompressRle8_LargeRun_SplitsAt255() {
    var data = new byte[300];
    Array.Fill(data, (byte)42);

    var compressed = RleCompressor.CompressRle8(data);

    // Should produce at least two run entries: one for 255 and one for 45
    // Plus EOL marker (0x00, 0x00)
    Assert.That(compressed.Length, Is.GreaterThanOrEqualTo(6));

    // Verify decompression roundtrip matches original
    var decompressed = RleCompressor.DecompressRle8(compressed, 300, 1);
    Assert.That(decompressed, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void DecompressRle8_RoundTrip_LargeData() {
    var data = new byte[4096];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 17); // pattern that creates mixed runs/literals

    var compressed = RleCompressor.CompressRle8(data);
    var decompressed = RleCompressor.DecompressRle8(compressed, 4096, 1);

    Assert.That(decompressed, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void CompressRle4_ProducesNonEmptyOutput() {
    // RLE4 indices: values in nibbles
    var indices = new byte[] { 0x01, 0x01, 0x01, 0x02, 0x02, 0x02, 0x03, 0x03 };

    var compressed = RleCompressor.CompressRle4(indices, indices.Length);

    Assert.That(compressed, Is.Not.Empty);
    Assert.That(compressed.Length, Is.LessThan(indices.Length * 2));
  }

  [Test]
  [Category("Unit")]
  public void CompressRle8_SingleByte_ProducesOutput() {
    var data = new byte[] { 42 };

    var compressed = RleCompressor.CompressRle8(data);

    Assert.That(compressed, Is.Not.Empty);
    // Should contain at least the encoded byte and EOL
    Assert.That(compressed.Length, Is.GreaterThanOrEqualTo(4));

    var decompressed = RleCompressor.DecompressRle8(compressed, 1, 1);
    Assert.That(decompressed[0], Is.EqualTo(42));
  }

  [Test]
  [Category("Unit")]
  public void EstimateCompressionRatio_MixedData_ReturnsMidRange() {
    var data = new byte[512];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 5 == 0 ? 0 : i % 256);

    var ratio = RleCompressor.EstimateCompressionRatio(data);

    // Ratio should be between 0.0 (perfect compression) and 2.0+ (expansion)
    Assert.That(ratio, Is.GreaterThan(0.0));
    Assert.That(ratio, Is.LessThan(3.0));
  }
}
