using System;
using System.IO;
using FileFormat.PalmPdb;

namespace FileFormat.PalmPdb.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb24_SmallImage() {
    var pixelData = new byte[2 * 2 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var original = new PalmPdbFile {
      Width = 2,
      Height = 2,
      PixelData = pixelData
    };

    var bytes = PalmPdbWriter.ToBytes(original);
    var restored = PalmPdbReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_NamePreserved() {
    var original = new PalmPdbFile {
      Width = 1,
      Height = 1,
      Name = "TestImage",
      PixelData = [0xAA, 0xBB, 0xCC]
    };

    var bytes = PalmPdbWriter.ToBytes(original);
    var restored = PalmPdbReader.FromBytes(bytes);

    Assert.That(restored.Name, Is.EqualTo("TestImage"));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdb");
    try {
      var pixelData = new byte[4 * 3 * 3];
      for (var i = 0; i < pixelData.Length; ++i)
        pixelData[i] = (byte)(i * 7 % 256);

      var original = new PalmPdbFile {
        Width = 4,
        Height = 3,
        Name = "FileTest",
        PixelData = pixelData
      };

      var bytes = PalmPdbWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = PalmPdbReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Name, Is.EqualTo(original.Name));
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
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new PalmPdbFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var bytes = PalmPdbWriter.ToBytes(original);
    var restored = PalmPdbReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SinglePixel() {
    var original = new PalmPdbFile {
      Width = 1,
      Height = 1,
      PixelData = [0xFF, 0x00, 0x80]
    };

    var bytes = PalmPdbWriter.ToBytes(original);
    var restored = PalmPdbReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(1));
    Assert.That(restored.Height, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_FromRawImage() {
    var pixelData = new byte[2 * 2 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 23 % 256);

    var original = new PalmPdbFile {
      Width = 2,
      Height = 2,
      PixelData = pixelData
    };

    var raw = PalmPdbFile.ToRawImage(original);
    var restored = PalmPdbFile.FromRawImage(raw);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
