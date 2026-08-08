using System;
using FileFormat.Core;

namespace FileFormat.Im5Visilog.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Gray8(int width, int height) {
    var data = new byte[width * height];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 5);

    return new() { Width = width, Height = height, Format = PixelFormat.Gray8, PixelData = data };
  }

  /// <summary>Sixteen-bit samples whose low bytes differ from their high ones, so halving them would
  /// be visible.</summary>
  private static RawImage _Gray16(int width, int height) {
    var data = new byte[width * height * 2];
    for (var i = 0; i < width * height; ++i) {
      data[i * 2] = (byte)(i * 3);
      data[i * 2 + 1] = (byte)(200 - i);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Gray16, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_EightBitSamples_ReproducesExactly() {
    var source = _Gray8(19, 5);
    var file = Im5VisilogFile.FromRawImage(source);
    var restored = Im5VisilogReader.FromBytes(Im5VisilogWriter.ToBytes(file));
    var decoded = Im5VisilogFile.ToRawImage(restored);

    Assert.That(restored.Depth, Is.EqualTo(8));
    for (var i = 0; i < source.PixelData.Length; ++i)
      Assert.That(decoded.PixelData[i * 3], Is.EqualTo(source.PixelData[i]), $"sample {i}");
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SixteenBitSamples_KeepsAllSixteenBits() {
    // Machine vision samples are measurements, so the low byte matters even though the decoder only
    // shows the high one.
    var source = _Gray16(8, 4);
    var file = Im5VisilogFile.FromRawImage(source);
    var restored = Im5VisilogReader.FromBytes(Im5VisilogWriter.ToBytes(file));

    Assert.That(restored.Depth, Is.EqualTo(16));
    for (var i = 0; i < 32; ++i) {
      var stored = restored.PixelData[i * 2] | (restored.PixelData[i * 2 + 1] << 8);
      var expected = (source.PixelData[i * 2] << 8) | source.PixelData[i * 2 + 1];
      Assert.That(stored, Is.EqualTo(expected), $"sample {i}");
    }
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_KeepsWhateverSizeItIsGiven() {
    var file = Im5VisilogFile.FromRawImage(_Gray8(101, 3));

    Assert.That((file.Width, file.Height), Is.EqualTo((101, 3)));
  }
}
