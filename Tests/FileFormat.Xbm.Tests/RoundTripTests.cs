using System;
using FileFormat.Xbm;

namespace FileFormat.Xbm.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_8x8_DimensionsAndPixelDataPreserved() {
    var pixelData = new byte[8];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 31 % 256);

    var original = new XbmFile {
      Width = 8,
      Height = 8,
      Name = "test",
      PixelData = pixelData
    };

    var bytes = XbmWriter.ToBytes(original);
    var restored = XbmReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Name, Is.EqualTo(original.Name));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_16x4_DimensionsAndPixelDataPreserved() {
    var bytesPerRow = 2; // 16 / 8
    var pixelData = new byte[bytesPerRow * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new XbmFile {
      Width = 16,
      Height = 4,
      Name = "icon",
      PixelData = pixelData
    };

    var bytes = XbmWriter.ToBytes(original);
    var restored = XbmReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(16));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_NonMultipleOf8Width_PreservesData() {
    var bytesPerRow = 2; // ceil(10/8) = 2
    var pixelData = new byte[bytesPerRow * 3];
    pixelData[0] = 0xFF;
    pixelData[1] = 0x03;
    pixelData[2] = 0xAA;
    pixelData[3] = 0x02;
    pixelData[4] = 0x55;
    pixelData[5] = 0x01;

    var original = new XbmFile {
      Width = 10,
      Height = 3,
      Name = "odd",
      PixelData = pixelData
    };

    var bytes = XbmWriter.ToBytes(original);
    var restored = XbmReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(10));
    Assert.That(restored.Height, Is.EqualTo(3));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithHotspot_PreservesHotspot() {
    var original = new XbmFile {
      Width = 8,
      Height = 8,
      Name = "cursor",
      HotspotX = 3,
      HotspotY = 5,
      PixelData = new byte[8]
    };

    var bytes = XbmWriter.ToBytes(original);
    var restored = XbmReader.FromBytes(bytes);

    Assert.That(restored.HotspotX, Is.EqualTo(3));
    Assert.That(restored.HotspotY, Is.EqualTo(5));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithoutHotspot_PreservesNullHotspot() {
    var original = new XbmFile {
      Width = 8,
      Height = 1,
      Name = "img",
      PixelData = [0xAB]
    };

    var bytes = XbmWriter.ToBytes(original);
    var restored = XbmReader.FromBytes(bytes);

    Assert.That(restored.HotspotX, Is.Null);
    Assert.That(restored.HotspotY, Is.Null);
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_1x1_SmallestPossibleImage() {
    var original = new XbmFile {
      Width = 1,
      Height = 1,
      Name = "dot",
      PixelData = [0x01]
    };

    var bytes = XbmWriter.ToBytes(original);
    var restored = XbmReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(1));
    Assert.That(restored.Height, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_NamePreserved() {
    var original = new XbmFile {
      Width = 8,
      Height = 1,
      Name = "my_custom_icon",
      PixelData = [0xFF]
    };

    var bytes = XbmWriter.ToBytes(original);
    var restored = XbmReader.FromBytes(bytes);

    Assert.That(restored.Name, Is.EqualTo("my_custom_icon"));
  }
}
