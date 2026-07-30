using System;
using System.IO;
using FileFormat.CameraRaw;

namespace FileFormat.CameraRaw.Tests;

[TestFixture]
public sealed class SonyDecompressorTests {

  [Test]
  [Category("Unit")]
  public void Decompress_NullData_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SonyDecompressor.Decompress(null!, 0, 100, 4, 4, 12));
  }

  [Test]
  [Category("Unit")]
  public void Decompress_StripOutOfBounds_ThrowsInvalidDataException() {
    var data = new byte[100];
    Assert.Throws<InvalidDataException>(() => SonyDecompressor.Decompress(data, 50, 200, 4, 4, 12));
  }

  [Test]
  [Category("Unit")]
  public void Decompress_NegativeOffset_ThrowsInvalidDataException() {
    var data = new byte[100];
    Assert.Throws<InvalidDataException>(() => SonyDecompressor.Decompress(data, -1, 50, 4, 4, 12));
  }

  [Test]
  [Category("Unit")]
  public void Decompress_OutputLength_Correct() {
    var width = 8;
    var height = 4;
    var data = new byte[2048];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 0xFF);

    var result = SonyDecompressor.Decompress(data, 0, data.Length, width, height, 12);

    Assert.That(result.Length, Is.EqualTo(width * height));
  }

  [Test]
  [Category("Unit")]
  public void Decompress_12Bit_ValuesWithinRange() {
    var width = 8;
    var height = 4;
    var data = new byte[2048];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 3 & 0xFF);

    var result = SonyDecompressor.Decompress(data, 0, data.Length, width, height, 12);

    for (var i = 0; i < result.Length; ++i)
      Assert.That(result[i], Is.LessThanOrEqualTo(4095), $"Sample {i} exceeds 12-bit range");
  }

  [Test]
  [Category("Unit")]
  public void Decompress_14Bit_ValuesWithinRange() {
    var width = 8;
    var height = 4;
    var data = new byte[2048];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 5 & 0xFF);

    var result = SonyDecompressor.Decompress(data, 0, data.Length, width, height, 14);

    for (var i = 0; i < result.Length; ++i)
      Assert.That(result[i], Is.LessThanOrEqualTo(16383), $"Sample {i} exceeds 14-bit range");
  }

  [Test]
  [Category("Unit")]
  public void Decompress_AllZeros_ProducesAllZeros() {
    var width = 4;
    var height = 2;
    var data = new byte[256]; // all zeros

    var result = SonyDecompressor.Decompress(data, 0, data.Length, width, height, 12);

    for (var i = 0; i < result.Length; ++i)
      Assert.That(result[i], Is.EqualTo(0), $"Sample {i} should be zero for all-zero input");
  }

  [Test]
  [Category("Unit")]
  public void Decompress_PartialStrip_Succeeds() {
    var width = 4;
    var height = 2;
    // Very small strip data - should not crash
    var data = new byte[8];

    Assert.DoesNotThrow(() => SonyDecompressor.Decompress(data, 0, data.Length, width, height, 12));
  }
}
