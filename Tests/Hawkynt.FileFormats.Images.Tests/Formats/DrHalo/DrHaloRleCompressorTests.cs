using System;
using FileFormat.DrHalo;

namespace FileFormat.DrHalo.Tests;

[TestFixture]
public sealed class DrHaloRleCompressorTests {

  [Test]
  [Category("Unit")]
  public void CompressScanline_Empty_ReturnsEmpty() {
    var result = DrHaloRleCompressor.CompressScanline(ReadOnlySpan<byte>.Empty);
    Assert.That(result.Length, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void CompressScanline_AllSame_CompressesWell() {
    var data = new byte[128];
    Array.Fill(data, (byte)42);

    var compressed = DrHaloRleCompressor.CompressScanline(data);

    Assert.That(compressed.Length, Is.LessThan(data.Length));
  }

  [Test]
  [Category("Unit")]
  public void DecompressScanline_RoundTrip_MixedData() {
    var data = new byte[32];
    for (var i = 0; i < 8; ++i)
      data[i] = 0xFF;
    for (var i = 8; i < 32; ++i)
      data[i] = (byte)(i * 7 % 256);

    var compressed = DrHaloRleCompressor.CompressScanline(data);
    var decompressed = DrHaloRleCompressor.DecompressScanline(compressed, 32);

    Assert.That(decompressed, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void DecompressScanline_RoundTrip_AllSame() {
    var data = new byte[256];
    Array.Fill(data, (byte)0xAB);

    var compressed = DrHaloRleCompressor.CompressScanline(data);
    var decompressed = DrHaloRleCompressor.DecompressScanline(compressed, 256);

    Assert.That(decompressed, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void DecompressScanline_RoundTrip_SingleByte() {
    var data = new byte[] { 99 };

    var compressed = DrHaloRleCompressor.CompressScanline(data);
    var decompressed = DrHaloRleCompressor.DecompressScanline(compressed, 1);

    Assert.That(decompressed[0], Is.EqualTo(99));
  }

  [Test]
  [Category("Unit")]
  public void CompressScanline_LargeRun_SplitsAt255() {
    var data = new byte[300];
    Array.Fill(data, (byte)77);

    var compressed = DrHaloRleCompressor.CompressScanline(data);
    var decompressed = DrHaloRleCompressor.DecompressScanline(compressed, 300);

    Assert.That(decompressed, Is.EqualTo(data));
    Assert.That(compressed.Length, Is.GreaterThanOrEqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void DecompressScanline_RoundTrip_AlternatingValues() {
    var data = new byte[64];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 2 == 0 ? 0xAA : 0xBB);

    var compressed = DrHaloRleCompressor.CompressScanline(data);
    var decompressed = DrHaloRleCompressor.DecompressScanline(compressed, 64);

    Assert.That(decompressed, Is.EqualTo(data));
  }
}
