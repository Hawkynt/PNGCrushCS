using System;
using FileFormat.SamCoupe;

namespace FileFormat.SamCoupe.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Mode4_DataPreserved() {
    var linearData = new byte[192 * 128];
    for (var i = 0; i < linearData.Length; ++i)
      linearData[i] = (byte)(i * 7 % 256);

    var original = new SamCoupeFile {
      Width = 256,
      Height = 192,
      Mode = SamCoupeMode.Mode4,
      PixelData = linearData
    };

    var bytes = SamCoupeWriter.ToBytes(original);
    var restored = SamCoupeReader.FromBytes(bytes, SamCoupeMode.Mode4);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Mode, Is.EqualTo(original.Mode));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Mode3_DataPreserved() {
    var linearData = new byte[192 * 128];
    for (var i = 0; i < linearData.Length; ++i)
      linearData[i] = (byte)(i * 13 % 256);

    var original = new SamCoupeFile {
      Width = 512,
      Height = 192,
      Mode = SamCoupeMode.Mode3,
      PixelData = linearData
    };

    var bytes = SamCoupeWriter.ToBytes(original);
    var restored = SamCoupeReader.FromBytes(bytes, SamCoupeMode.Mode3);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Mode, Is.EqualTo(original.Mode));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros_DataPreserved() {
    var linearData = new byte[192 * 128];

    var original = new SamCoupeFile {
      Width = 256,
      Height = 192,
      Mode = SamCoupeMode.Mode4,
      PixelData = linearData
    };

    var bytes = SamCoupeWriter.ToBytes(original);
    var restored = SamCoupeReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PageInterleave_EvenOddSeparated() {
    var linearData = new byte[192 * 128];
    // Mark even rows with 0xAA, odd rows with 0x55
    for (var row = 0; row < 192; ++row)
      for (var col = 0; col < 128; ++col)
        linearData[row * 128 + col] = (byte)(row % 2 == 0 ? 0xAA : 0x55);

    var original = new SamCoupeFile {
      Width = 256,
      Height = 192,
      Mode = SamCoupeMode.Mode4,
      PixelData = linearData
    };

    var bytes = SamCoupeWriter.ToBytes(original);

    // Verify even lines are in first page (0..12287)
    for (var i = 0; i < 12288; ++i)
      Assert.That(bytes[i], Is.EqualTo(0xAA), $"First page byte {i} should be even-row marker");

    // Verify odd lines are in second page (12288..24575)
    for (var i = 12288; i < 24576; ++i)
      Assert.That(bytes[i], Is.EqualTo(0x55), $"Second page byte {i} should be odd-row marker");

    // Round-trip readback
    var restored = SamCoupeReader.FromBytes(bytes, SamCoupeMode.Mode4);
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllOnes_DataPreserved() {
    var linearData = new byte[192 * 128];
    Array.Fill(linearData, (byte)0xFF);

    var original = new SamCoupeFile {
      Width = 256,
      Height = 192,
      Mode = SamCoupeMode.Mode4,
      PixelData = linearData
    };

    var bytes = SamCoupeWriter.ToBytes(original);
    var restored = SamCoupeReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SinglePixelPerRow_AllRows() {
    var linearData = new byte[192 * 128];
    for (var row = 0; row < 192; ++row)
      linearData[row * 128] = (byte)(row % 256);

    var original = new SamCoupeFile {
      Width = 256,
      Height = 192,
      Mode = SamCoupeMode.Mode4,
      PixelData = linearData
    };

    var bytes = SamCoupeWriter.ToBytes(original);
    var restored = SamCoupeReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
