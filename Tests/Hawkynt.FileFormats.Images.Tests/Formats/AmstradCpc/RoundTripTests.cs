using System;
using FileFormat.AmstradCpc;

namespace FileFormat.AmstradCpc.Tests;

[TestFixture]
public sealed class RoundTripTests {

  private const int _LINEAR_SIZE = 200 * 80; // 16000 bytes: 200 scanlines * 80 bytes each

  [Test]
  [Category("Integration")]
  public void RoundTrip_Mode1_DataPreserved() {
    var linearData = new byte[_LINEAR_SIZE];
    for (var i = 0; i < linearData.Length; ++i)
      linearData[i] = (byte)(i * 7 % 256);

    var original = new AmstradCpcFile {
      Width = 320,
      Height = 200,
      Mode = AmstradCpcMode.Mode1,
      PixelData = linearData
    };

    var bytes = AmstradCpcWriter.ToBytes(original);
    var restored = AmstradCpcReader.FromBytes(bytes, AmstradCpcMode.Mode1);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Mode, Is.EqualTo(original.Mode));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Mode0_DataPreserved() {
    var linearData = new byte[_LINEAR_SIZE];
    for (var i = 0; i < linearData.Length; ++i)
      linearData[i] = (byte)(i * 13 % 256);

    var original = new AmstradCpcFile {
      Width = 160,
      Height = 200,
      Mode = AmstradCpcMode.Mode0,
      PixelData = linearData
    };

    var bytes = AmstradCpcWriter.ToBytes(original);
    var restored = AmstradCpcReader.FromBytes(bytes, AmstradCpcMode.Mode0);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Mode, Is.EqualTo(original.Mode));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Mode2_DataPreserved() {
    var linearData = new byte[_LINEAR_SIZE];
    for (var i = 0; i < linearData.Length; ++i)
      linearData[i] = (byte)(i * 3 % 256);

    var original = new AmstradCpcFile {
      Width = 640,
      Height = 200,
      Mode = AmstradCpcMode.Mode2,
      PixelData = linearData
    };

    var bytes = AmstradCpcWriter.ToBytes(original);
    var restored = AmstradCpcReader.FromBytes(bytes, AmstradCpcMode.Mode2);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Mode, Is.EqualTo(original.Mode));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros_DataPreserved() {
    var linearData = new byte[_LINEAR_SIZE];

    var original = new AmstradCpcFile {
      Width = 320,
      Height = 200,
      Mode = AmstradCpcMode.Mode1,
      PixelData = linearData
    };

    var bytes = AmstradCpcWriter.ToBytes(original);
    var restored = AmstradCpcReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WriterOutput_IsAlways16384Bytes() {
    var linearData = new byte[_LINEAR_SIZE];
    for (var i = 0; i < linearData.Length; ++i)
      linearData[i] = (byte)i;

    var file = new AmstradCpcFile {
      Width = 320,
      Height = 200,
      Mode = AmstradCpcMode.Mode1,
      PixelData = linearData
    };

    var bytes = AmstradCpcWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(16384));
  }
}
