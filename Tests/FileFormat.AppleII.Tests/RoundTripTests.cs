using System;
using FileFormat.AppleII;

namespace FileFormat.AppleII.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Hgr() {
    var pixelData = new byte[192 * 40];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new AppleIIFile {
      Width = 280,
      Height = 192,
      Mode = AppleIIMode.Hgr,
      PixelData = pixelData
    };

    var bytes = AppleIIWriter.ToBytes(original);
    var restored = AppleIIReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Mode, Is.EqualTo(original.Mode));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Dhgr() {
    var pixelData = new byte[192 * 80];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new AppleIIFile {
      Width = 560,
      Height = 192,
      Mode = AppleIIMode.Dhgr,
      PixelData = pixelData
    };

    var bytes = AppleIIWriter.ToBytes(original);
    var restored = AppleIIReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Mode, Is.EqualTo(original.Mode));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new AppleIIFile {
      Width = 280,
      Height = 192,
      Mode = AppleIIMode.Hgr,
      PixelData = new byte[192 * 40]
    };

    var bytes = AppleIIWriter.ToBytes(original);
    var restored = AppleIIReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(280));
    Assert.That(restored.Height, Is.EqualTo(192));
    Assert.That(restored.Mode, Is.EqualTo(AppleIIMode.Hgr));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
