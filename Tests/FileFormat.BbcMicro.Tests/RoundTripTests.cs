using System;
using FileFormat.BbcMicro;

namespace FileFormat.BbcMicro.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Mode1_DataPreserved() {
    var charCols = 80;
    var linearData = new byte[256 * charCols];
    for (var i = 0; i < linearData.Length; ++i)
      linearData[i] = (byte)(i * 7 % 256);

    var original = new BbcMicroFile {
      Width = 320,
      Height = 256,
      Mode = BbcMicroMode.Mode1,
      PixelData = linearData
    };

    var bytes = BbcMicroWriter.ToBytes(original);
    var restored = BbcMicroReader.FromBytes(bytes, BbcMicroMode.Mode1);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Mode, Is.EqualTo(original.Mode));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Mode0_DataPreserved() {
    var charCols = 80;
    var linearData = new byte[256 * charCols];
    for (var i = 0; i < linearData.Length; ++i)
      linearData[i] = (byte)(i * 13 % 256);

    var original = new BbcMicroFile {
      Width = 640,
      Height = 256,
      Mode = BbcMicroMode.Mode0,
      PixelData = linearData
    };

    var bytes = BbcMicroWriter.ToBytes(original);
    var restored = BbcMicroReader.FromBytes(bytes, BbcMicroMode.Mode0);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Mode, Is.EqualTo(original.Mode));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Mode4_DataPreserved() {
    var charCols = 40;
    var linearData = new byte[256 * charCols];
    for (var i = 0; i < linearData.Length; ++i)
      linearData[i] = (byte)(i * 3 % 256);

    var original = new BbcMicroFile {
      Width = 320,
      Height = 256,
      Mode = BbcMicroMode.Mode4,
      PixelData = linearData
    };

    var bytes = BbcMicroWriter.ToBytes(original);
    var restored = BbcMicroReader.FromBytes(bytes, BbcMicroMode.Mode4);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Mode, Is.EqualTo(original.Mode));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Mode2_DataPreserved() {
    var charCols = 80;
    var linearData = new byte[256 * charCols];
    for (var i = 0; i < linearData.Length; ++i)
      linearData[i] = (byte)(i * 11 % 256);

    var original = new BbcMicroFile {
      Width = 160,
      Height = 256,
      Mode = BbcMicroMode.Mode2,
      PixelData = linearData
    };

    var bytes = BbcMicroWriter.ToBytes(original);
    var restored = BbcMicroReader.FromBytes(bytes, BbcMicroMode.Mode2);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Mode, Is.EqualTo(original.Mode));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Mode5_DataPreserved() {
    var charCols = 40;
    var linearData = new byte[256 * charCols];
    for (var i = 0; i < linearData.Length; ++i)
      linearData[i] = (byte)(i * 17 % 256);

    var original = new BbcMicroFile {
      Width = 160,
      Height = 256,
      Mode = BbcMicroMode.Mode5,
      PixelData = linearData
    };

    var bytes = BbcMicroWriter.ToBytes(original);
    var restored = BbcMicroReader.FromBytes(bytes, BbcMicroMode.Mode5);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Mode, Is.EqualTo(original.Mode));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros_DataPreserved() {
    var linearData = new byte[256 * 80];

    var original = new BbcMicroFile {
      Width = 320,
      Height = 256,
      Mode = BbcMicroMode.Mode1,
      PixelData = linearData
    };

    var bytes = BbcMicroWriter.ToBytes(original);
    var restored = BbcMicroReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WriterOutput_IsCorrectSizeForMode1() {
    var linearData = new byte[256 * 80];
    for (var i = 0; i < linearData.Length; ++i)
      linearData[i] = (byte)i;

    var file = new BbcMicroFile {
      Width = 320,
      Height = 256,
      Mode = BbcMicroMode.Mode1,
      PixelData = linearData
    };

    var bytes = BbcMicroWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(BbcMicroFile.ScreenSizeModes012));
  }
}
