using System;
using System.IO;
using FileFormat.MetaImage;

namespace FileFormat.MetaImage.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale() {
    var pixelData = new byte[3 * 2];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 40);

    var original = new MetaImageFile {
      Width = 3,
      Height = 2,
      ElementType = MetaImageElementType.MetUChar,
      Channels = 1,
      PixelData = pixelData,
    };

    var bytes = MetaImageWriter.ToBytes(original);
    var restored = MetaImageReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.ElementType, Is.EqualTo(original.ElementType));
    Assert.That(restored.Channels, Is.EqualTo(original.Channels));
    Assert.That(restored.IsCompressed, Is.False);
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb() {
    var pixelData = new byte[2 * 2 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var original = new MetaImageFile {
      Width = 2,
      Height = 2,
      ElementType = MetaImageElementType.MetUChar,
      Channels = 3,
      PixelData = pixelData,
    };

    var bytes = MetaImageWriter.ToBytes(original);
    var restored = MetaImageReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Channels, Is.EqualTo(3));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Compressed() {
    var pixelData = new byte[4 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new MetaImageFile {
      Width = 4,
      Height = 4,
      ElementType = MetaImageElementType.MetUChar,
      Channels = 1,
      IsCompressed = true,
      PixelData = pixelData,
    };

    var bytes = MetaImageWriter.ToBytes(original);
    var restored = MetaImageReader.FromBytes(bytes);

    Assert.That(restored.IsCompressed, Is.True);
    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[2 * 2];
    pixelData[0] = 0x10;
    pixelData[1] = 0x20;
    pixelData[2] = 0x30;
    pixelData[3] = 0x40;

    var original = new MetaImageFile {
      Width = 2,
      Height = 2,
      ElementType = MetaImageElementType.MetUChar,
      Channels = 1,
      PixelData = pixelData,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mha");
    try {
      var bytes = MetaImageWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);
      var restored = MetaImageReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargerImage() {
    var width = 64;
    var height = 48;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new MetaImageFile {
      Width = width,
      Height = height,
      ElementType = MetaImageElementType.MetUChar,
      Channels = 3,
      PixelData = pixelData,
    };

    var bytes = MetaImageWriter.ToBytes(original);
    var restored = MetaImageReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.Channels, Is.EqualTo(3));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MetShort() {
    var pixelData = new byte[2 * 2 * 2]; // 2x2 image, 2 bytes per sample
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 31 % 256);

    var original = new MetaImageFile {
      Width = 2,
      Height = 2,
      ElementType = MetaImageElementType.MetShort,
      Channels = 1,
      PixelData = pixelData,
    };

    var bytes = MetaImageWriter.ToBytes(original);
    var restored = MetaImageReader.FromBytes(bytes);

    Assert.That(restored.ElementType, Is.EqualTo(MetaImageElementType.MetShort));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Grayscale() {
    var original = new MetaImageFile {
      Width = 2,
      Height = 2,
      ElementType = MetaImageElementType.MetUChar,
      Channels = 1,
      PixelData = [0x10, 0x20, 0x30, 0x40],
    };

    var raw = MetaImageFile.ToRawImage(original);
    var restored = MetaImageFile.FromRawImage(raw);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Channels, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Rgb() {
    var pixelData = new byte[2 * 2 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 19 % 256);

    var original = new MetaImageFile {
      Width = 2,
      Height = 2,
      ElementType = MetaImageElementType.MetUChar,
      Channels = 3,
      PixelData = pixelData,
    };

    var raw = MetaImageFile.ToRawImage(original);
    var restored = MetaImageFile.FromRawImage(raw);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Channels, Is.EqualTo(3));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
