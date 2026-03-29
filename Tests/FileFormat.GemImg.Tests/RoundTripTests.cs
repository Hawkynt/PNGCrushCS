using System;
using FileFormat.GemImg;

namespace FileFormat.GemImg.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Monochrome_1Plane() {
    var bytesPerRow = 2; // 16 pixels / 8
    var height = 4;
    var pixelData = new byte[bytesPerRow * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 37 % 256);

    var original = new GemImgFile {
      Version = 1,
      Width = 16,
      Height = height,
      NumPlanes = 1,
      PatternLength = 2,
      PixelWidth = 85,
      PixelHeight = 85,
      PixelData = pixelData
    };

    var bytes = GemImgWriter.ToBytes(original);
    var restored = GemImgReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.NumPlanes, Is.EqualTo(original.NumPlanes));
    Assert.That(restored.Version, Is.EqualTo(original.Version));
    Assert.That(restored.PatternLength, Is.EqualTo(original.PatternLength));
    Assert.That(restored.PixelWidth, Is.EqualTo(original.PixelWidth));
    Assert.That(restored.PixelHeight, Is.EqualTo(original.PixelHeight));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultiPlane() {
    var bytesPerRow = 4; // 32 pixels / 8
    var height = 3;
    var numPlanes = 4;
    var pixelData = new byte[numPlanes * bytesPerRow * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new GemImgFile {
      Version = 1,
      Width = 32,
      Height = height,
      NumPlanes = numPlanes,
      PatternLength = 2,
      PixelWidth = 85,
      PixelHeight = 85,
      PixelData = pixelData
    };

    var bytes = GemImgWriter.ToBytes(original);
    var restored = GemImgReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.NumPlanes, Is.EqualTo(original.NumPlanes));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SinglePixelRow() {
    var pixelData = new byte[] { 0xAA }; // 8 pixels, 1 row, 1 plane

    var original = new GemImgFile {
      Version = 1,
      Width = 8,
      Height = 1,
      NumPlanes = 1,
      PatternLength = 1,
      PixelWidth = 85,
      PixelHeight = 85,
      PixelData = pixelData
    };

    var bytes = GemImgWriter.ToBytes(original);
    var restored = GemImgReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(8));
    Assert.That(restored.Height, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PixelAspectRatio_Preserved() {
    var original = new GemImgFile {
      Version = 1,
      Width = 8,
      Height = 1,
      NumPlanes = 1,
      PatternLength = 1,
      PixelWidth = 170,
      PixelHeight = 85,
      PixelData = new byte[1]
    };

    var bytes = GemImgWriter.ToBytes(original);
    var restored = GemImgReader.FromBytes(bytes);

    Assert.That(restored.PixelWidth, Is.EqualTo(170));
    Assert.That(restored.PixelHeight, Is.EqualTo(85));
  }
}
