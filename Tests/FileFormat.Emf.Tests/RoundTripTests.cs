using System;
using System.IO;
using FileFormat.Emf;

namespace FileFormat.Emf.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SmallImage() {
    var width = 2;
    var height = 2;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var original = new EmfFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var bytes = EmfWriter.ToBytes(original);
    var restored = EmfReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var width = 3;
    var height = 2;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 37 % 256);

    var original = new EmfFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".emf");
    try {
      var bytes = EmfWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = EmfReader.FromFile(new FileInfo(tempPath));

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
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new EmfFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var bytes = EmfWriter.ToBytes(original);
    var restored = EmfReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var width = 4;
    var height = 3;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new EmfFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var raw = EmfFile.ToRawImage(original);
    var fromRaw = EmfFile.FromRawImage(raw);

    Assert.That(fromRaw.Width, Is.EqualTo(original.Width));
    Assert.That(fromRaw.Height, Is.EqualTo(original.Height));
    Assert.That(fromRaw.PixelData, Is.EqualTo(original.PixelData));
  }
}
