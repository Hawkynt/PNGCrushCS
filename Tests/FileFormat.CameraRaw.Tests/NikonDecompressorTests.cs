using System;
using System.IO;
using FileFormat.CameraRaw;

namespace FileFormat.CameraRaw.Tests;

[TestFixture]
public sealed class NikonDecompressorTests {

  [Test]
  [Category("Unit")]
  public void Decompress_NullData_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NikonDecompressor.Decompress(null!, 0, 100, 4, 4, 12, null, false));
  }

  [Test]
  [Category("Unit")]
  public void Decompress_StripOutOfBounds_ThrowsInvalidDataException() {
    var data = new byte[100];
    Assert.Throws<InvalidDataException>(() => NikonDecompressor.Decompress(data, 50, 200, 4, 4, 12, null, false));
  }

  [Test]
  [Category("Unit")]
  public void Decompress_NegativeOffset_ThrowsInvalidDataException() {
    var data = new byte[100];
    Assert.Throws<InvalidDataException>(() => NikonDecompressor.Decompress(data, -1, 50, 4, 4, 12, null, false));
  }

  [Test]
  [Category("Unit")]
  public void Decompress_OutputLength_Correct() {
    // Create some data that the decompressor can read (may produce zeros, but at least outputs correct size)
    var width = 4;
    var height = 4;
    var data = new byte[1024];
    // Fill with some non-zero pattern to prevent degenerate behavior
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 0xFF);

    var result = NikonDecompressor.Decompress(data, 0, data.Length, width, height, 12, null, false);

    Assert.That(result.Length, Is.EqualTo(width * height));
  }

  [Test]
  [Category("Unit")]
  public void Decompress_12Bit_ValuesWithinRange() {
    var width = 4;
    var height = 2;
    var data = new byte[512];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 7 & 0xFF);

    var result = NikonDecompressor.Decompress(data, 0, data.Length, width, height, 12, null, false);

    for (var i = 0; i < result.Length; ++i)
      Assert.That(result[i], Is.LessThanOrEqualTo(4095), $"Sample {i} exceeds 12-bit range");
  }

  [Test]
  [Category("Unit")]
  public void Decompress_14Bit_ValuesWithinRange() {
    var width = 4;
    var height = 2;
    var data = new byte[512];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 13 & 0xFF);

    var result = NikonDecompressor.Decompress(data, 0, data.Length, width, height, 14, null, false);

    for (var i = 0; i < result.Length; ++i)
      Assert.That(result[i], Is.LessThanOrEqualTo(16383), $"Sample {i} exceeds 14-bit range");
  }

  [Test]
  [Category("Unit")]
  public void Decompress_WithCurve_AppliesCurve() {
    var width = 2;
    var height = 2;
    var data = new byte[256];

    // Create a simple curve that maps every value to a fixed value
    var curve = new ushort[4096];
    for (var i = 0; i < curve.Length; ++i)
      curve[i] = 1000;

    var result = NikonDecompressor.Decompress(data, 0, data.Length, width, height, 12, curve, false);

    // All output values should be 1000 after curve application
    for (var i = 0; i < result.Length; ++i)
      Assert.That(result[i], Is.EqualTo(1000), $"Sample {i} was not mapped by curve");
  }

  [Test]
  [Category("Unit")]
  public void Decompress_LossyFlag_DoesNotCrash() {
    var width = 4;
    var height = 2;
    var data = new byte[256];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 0xFF);

    Assert.DoesNotThrow(() => NikonDecompressor.Decompress(data, 0, data.Length, width, height, 12, null, true));
  }
}
