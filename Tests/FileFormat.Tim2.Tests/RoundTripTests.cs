using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Tim2;

namespace FileFormat.Tim2.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb32() {
    var pixelData = new byte[4 * 2 * 4]; // 4x2, RGBA
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new Tim2File {
      Version = 4,
      Alignment = 0,
      Pictures = [
        new Tim2Picture {
          Width = 4,
          Height = 2,
          Format = Tim2Format.Rgb32,
          MipmapCount = 1,
          PixelData = pixelData
        }
      ]
    };

    var bytes = Tim2Writer.ToBytes(original);
    var restored = Tim2Reader.FromBytes(bytes);

    Assert.That(restored.Version, Is.EqualTo(original.Version));
    Assert.That(restored.Pictures, Has.Count.EqualTo(1));
    Assert.That(restored.Pictures[0].Width, Is.EqualTo(4));
    Assert.That(restored.Pictures[0].Height, Is.EqualTo(2));
    Assert.That(restored.Pictures[0].Format, Is.EqualTo(Tim2Format.Rgb32));
    Assert.That(restored.Pictures[0].PixelData, Is.EqualTo(original.Pictures[0].PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Indexed8() {
    var pixelData = new byte[4 * 2]; // 4x2, 1 byte per pixel
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var paletteData = new byte[256 * 4]; // 256 colors, RGBA
    for (var i = 0; i < paletteData.Length; ++i)
      paletteData[i] = (byte)(i * 7 % 256);

    var original = new Tim2File {
      Version = 4,
      Alignment = 0,
      Pictures = [
        new Tim2Picture {
          Width = 4,
          Height = 2,
          Format = Tim2Format.Indexed8,
          MipmapCount = 1,
          PixelData = pixelData,
          PaletteData = paletteData,
          PaletteColors = 256
        }
      ]
    };

    var bytes = Tim2Writer.ToBytes(original);
    var restored = Tim2Reader.FromBytes(bytes);

    Assert.That(restored.Pictures, Has.Count.EqualTo(1));
    Assert.That(restored.Pictures[0].Width, Is.EqualTo(4));
    Assert.That(restored.Pictures[0].Height, Is.EqualTo(2));
    Assert.That(restored.Pictures[0].Format, Is.EqualTo(Tim2Format.Indexed8));
    Assert.That(restored.Pictures[0].PixelData, Is.EqualTo(original.Pictures[0].PixelData));
    Assert.That(restored.Pictures[0].PaletteData, Is.EqualTo(original.Pictures[0].PaletteData));
    Assert.That(restored.Pictures[0].PaletteColors, Is.EqualTo(256));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultiplePictures() {
    var pictures = new List<Tim2Picture>();
    for (var p = 0; p < 3; ++p) {
      var pixelData = new byte[2 * 2 * 4];
      for (var i = 0; i < pixelData.Length; ++i)
        pixelData[i] = (byte)((i + p * 50) % 256);

      pictures.Add(new Tim2Picture {
        Width = 2,
        Height = 2,
        Format = Tim2Format.Rgb32,
        MipmapCount = 1,
        PixelData = pixelData
      });
    }

    var original = new Tim2File {
      Version = 4,
      Alignment = 0,
      Pictures = pictures.AsReadOnly()
    };

    var bytes = Tim2Writer.ToBytes(original);
    var restored = Tim2Reader.FromBytes(bytes);

    Assert.That(restored.Pictures, Has.Count.EqualTo(3));
    for (var i = 0; i < 3; ++i) {
      Assert.That(restored.Pictures[i].Width, Is.EqualTo(2));
      Assert.That(restored.Pictures[i].Height, Is.EqualTo(2));
      Assert.That(restored.Pictures[i].Format, Is.EqualTo(Tim2Format.Rgb32));
      Assert.That(restored.Pictures[i].PixelData, Is.EqualTo(original.Pictures[i].PixelData));
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[4 * 2 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 5 % 256);

    var original = new Tim2File {
      Version = 4,
      Alignment = 0,
      Pictures = [
        new Tim2Picture {
          Width = 4,
          Height = 2,
          Format = Tim2Format.Rgb32,
          MipmapCount = 1,
          PixelData = pixelData
        }
      ]
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tm2");
    try {
      var bytes = Tim2Writer.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);
      var restored = Tim2Reader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Pictures, Has.Count.EqualTo(1));
      Assert.That(restored.Pictures[0].Width, Is.EqualTo(4));
      Assert.That(restored.Pictures[0].Height, Is.EqualTo(2));
      Assert.That(restored.Pictures[0].PixelData, Is.EqualTo(original.Pictures[0].PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
