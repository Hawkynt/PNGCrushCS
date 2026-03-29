using System;
using System.IO;
using FileFormat.Bsave;

namespace FileFormat.Bsave.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Vga_PreservesData() {
    var pixelData = new byte[64000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var original = new BsaveFile {
      Width = 320,
      Height = 200,
      Mode = BsaveMode.Vga320x200x256,
      PixelData = pixelData
    };

    var bytes = BsaveWriter.ToBytes(original);
    var restored = BsaveReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(320));
    Assert.That(restored.Height, Is.EqualTo(200));
    Assert.That(restored.Mode, Is.EqualTo(BsaveMode.Vga320x200x256));
    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Cga_PreservesData() {
    var pixelData = new byte[16384];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 3 % 256);

    var original = new BsaveFile {
      Width = 320,
      Height = 200,
      Mode = BsaveMode.Cga320x200x4,
      PixelData = pixelData
    };

    var bytes = BsaveWriter.ToBytes(original);
    var restored = BsaveReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(320));
    Assert.That(restored.Height, Is.EqualTo(200));
    Assert.That(restored.Mode, Is.EqualTo(BsaveMode.Cga320x200x4));
    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_PreservesData() {
    var pixelData = new byte[64000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new BsaveFile {
      Width = 320,
      Height = 200,
      Mode = BsaveMode.Vga320x200x256,
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bsv");
    try {
      var bytes = BsaveWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);
      var restored = BsaveReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(320));
      Assert.That(restored.Height, Is.EqualTo(200));
      Assert.That(restored.PixelData, Is.EqualTo(pixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
