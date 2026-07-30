using System;
using System.IO;
using FileFormat.Ingr;

namespace FileFormat.Ingr.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb24() {
    var width = 4;
    var height = 3;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new IngrFile {
      Width = width,
      Height = height,
      DataType = IngrDataType.Rgb24,
      PixelData = pixelData
    };

    var bytes = IngrWriter.ToBytes(original);
    var restored = IngrReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.DataType, Is.EqualTo(original.DataType));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gray8() {
    var width = 8;
    var height = 4;
    var pixelData = new byte[width * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new IngrFile {
      Width = width,
      Height = height,
      DataType = IngrDataType.ByteData,
      PixelData = pixelData
    };

    var bytes = IngrWriter.ToBytes(original);
    var restored = IngrReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.DataType, Is.EqualTo(original.DataType));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cit");
    try {
      var original = new IngrFile {
        Width = 3,
        Height = 2,
        DataType = IngrDataType.Rgb24,
        PixelData = [
          255, 0, 0, 0, 255, 0, 0, 0, 255,
          128, 128, 128, 64, 64, 64, 32, 32, 32
        ]
      };

      var bytes = IngrWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = IngrReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.DataType, Is.EqualTo(original.DataType));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Rgb24() {
    var rawImage = new FileFormat.Core.RawImage {
      Width = 2,
      Height = 2,
      Format = FileFormat.Core.PixelFormat.Rgb24,
      PixelData = [255, 0, 0, 0, 255, 0, 0, 0, 255, 128, 128, 128]
    };

    var ingrFile = IngrFile.FromRawImage(rawImage);
    var restored = IngrFile.ToRawImage(ingrFile);

    Assert.That(restored.Width, Is.EqualTo(rawImage.Width));
    Assert.That(restored.Height, Is.EqualTo(rawImage.Height));
    Assert.That(restored.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Rgb24));
    Assert.That(restored.PixelData, Is.EqualTo(rawImage.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Gray8() {
    var rawImage = new FileFormat.Core.RawImage {
      Width = 3,
      Height = 2,
      Format = FileFormat.Core.PixelFormat.Gray8,
      PixelData = [10, 20, 30, 40, 50, 60]
    };

    var ingrFile = IngrFile.FromRawImage(rawImage);
    var restored = IngrFile.ToRawImage(ingrFile);

    Assert.That(restored.Width, Is.EqualTo(rawImage.Width));
    Assert.That(restored.Height, Is.EqualTo(rawImage.Height));
    Assert.That(restored.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Gray8));
    Assert.That(restored.PixelData, Is.EqualTo(rawImage.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargeImage() {
    var width = 64;
    var height = 32;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new IngrFile {
      Width = width,
      Height = height,
      DataType = IngrDataType.Rgb24,
      PixelData = pixelData
    };

    var bytes = IngrWriter.ToBytes(original);
    var restored = IngrReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
