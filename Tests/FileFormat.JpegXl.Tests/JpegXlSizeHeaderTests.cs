using System;
using FileFormat.JpegXl;

namespace FileFormat.JpegXl.Tests;

[TestFixture]
public sealed class JpegXlSizeHeaderTests {

  [Test]
  [Category("Unit")]
  public void EncodeDecodeRoundTrip_SmallSquare() {
    // 8x8: height=8 (div8=0), ratio=1 (1:1)
    var encoded = JpegXlSizeHeader.Encode(8, 8);
    var (width, height, _) = JpegXlSizeHeader.Decode(encoded);

    Assert.Multiple(() => {
      Assert.That(width, Is.EqualTo(8));
      Assert.That(height, Is.EqualTo(8));
    });
  }

  [Test]
  [Category("Unit")]
  public void EncodeDecodeRoundTrip_SmallDoubleWidth() {
    // 256x128: height=128 (div8=15), ratio=7 (2:1)
    var encoded = JpegXlSizeHeader.Encode(256, 128);
    var (width, height, _) = JpegXlSizeHeader.Decode(encoded);

    Assert.Multiple(() => {
      Assert.That(width, Is.EqualTo(256));
      Assert.That(height, Is.EqualTo(128));
    });
  }

  [Test]
  [Category("Unit")]
  public void EncodeDecodeRoundTrip_LargeNonRatio() {
    // 320x240: not a known ratio, uses large encoding
    var encoded = JpegXlSizeHeader.Encode(320, 240);
    var (width, height, _) = JpegXlSizeHeader.Decode(encoded);

    Assert.Multiple(() => {
      Assert.That(width, Is.EqualTo(320));
      Assert.That(height, Is.EqualTo(240));
    });
  }

  [Test]
  [Category("Unit")]
  public void EncodeDecodeRoundTrip_1x1() {
    var encoded = JpegXlSizeHeader.Encode(1, 1);
    var (width, height, _) = JpegXlSizeHeader.Decode(encoded);

    Assert.Multiple(() => {
      Assert.That(width, Is.EqualTo(1));
      Assert.That(height, Is.EqualTo(1));
    });
  }

  [Test]
  [Category("Unit")]
  public void EncodeDecodeRoundTrip_1920x1080() {
    var encoded = JpegXlSizeHeader.Encode(1920, 1080);
    var (width, height, _) = JpegXlSizeHeader.Decode(encoded);

    Assert.Multiple(() => {
      Assert.That(width, Is.EqualTo(1920));
      Assert.That(height, Is.EqualTo(1080));
    });
  }

  [Test]
  [Category("Unit")]
  public void EncodeDecodeRoundTrip_4096x2160() {
    var encoded = JpegXlSizeHeader.Encode(4096, 2160);
    var (width, height, _) = JpegXlSizeHeader.Decode(encoded);

    Assert.Multiple(() => {
      Assert.That(width, Is.EqualTo(4096));
      Assert.That(height, Is.EqualTo(2160));
    });
  }

  [Test]
  [Category("Unit")]
  public void EncodeDecodeRoundTrip_100x75() {
    var encoded = JpegXlSizeHeader.Encode(100, 75);
    var (width, height, _) = JpegXlSizeHeader.Decode(encoded);

    Assert.Multiple(() => {
      Assert.That(width, Is.EqualTo(100));
      Assert.That(height, Is.EqualTo(75));
    });
  }

  [Test]
  [Category("Unit")]
  public void Decode_BytesConsumed_IsPositive() {
    var encoded = JpegXlSizeHeader.Encode(640, 480);
    var (_, _, bytesConsumed) = JpegXlSizeHeader.Decode(encoded);
    Assert.That(bytesConsumed, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void Decode_EmptyData_Throws() {
    Assert.Throws<InvalidOperationException>(() => JpegXlSizeHeader.Decode(ReadOnlySpan<byte>.Empty));
  }

  [Test]
  [Category("Unit")]
  public void Encode_SmallEncoding_ProducesFewerBytes() {
    // 16x16 should use small encoding (1 byte or so)
    var small = JpegXlSizeHeader.Encode(16, 16);
    // 320x240 should use large encoding (more bytes)
    var large = JpegXlSizeHeader.Encode(320, 240);
    Assert.That(small.Length, Is.LessThanOrEqualTo(large.Length));
  }

  [Test]
  [Category("Unit")]
  public void EncodeDecodeRoundTrip_512x512() {
    // 512 not divisible result in small path, uses large
    var encoded = JpegXlSizeHeader.Encode(512, 512);
    var (width, height, _) = JpegXlSizeHeader.Decode(encoded);

    Assert.Multiple(() => {
      Assert.That(width, Is.EqualTo(512));
      Assert.That(height, Is.EqualTo(512));
    });
  }
}
