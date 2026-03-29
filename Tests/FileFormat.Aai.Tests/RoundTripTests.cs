using System;
using System.IO;
using FileFormat.Aai;

namespace FileFormat.Aai.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SpecificPixels() {
    var pixelData = new byte[2 * 2 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var original = new AaiFile {
      Width = 2,
      Height = 2,
      PixelData = pixelData
    };

    var bytes = AaiWriter.ToBytes(original);
    var restored = AaiReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new AaiFile {
      Width = 3,
      Height = 2,
      PixelData = new byte[3 * 2 * 4]
    };

    var bytes = AaiWriter.ToBytes(original);
    var restored = AaiReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(3));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".aai");
    try {
      var pixels = new byte[4 * 3 * 4];
      for (var i = 0; i < pixels.Length; ++i)
        pixels[i] = (byte)(i * 13 % 256);

      var original = new AaiFile {
        Width = 4,
        Height = 3,
        PixelData = pixels
      };

      var bytes = AaiWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = AaiReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
