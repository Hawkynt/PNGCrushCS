using System;
using System.IO;
using FileFormat.Mtv;

namespace FileFormat.Mtv.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_1x1() {
    var original = new MtvFile {
      Width = 1,
      Height = 1,
      PixelData = [42, 84, 126]
    };

    var bytes = MtvWriter.ToBytes(original);
    var restored = MtvReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargeImage() {
    var width = 64;
    var height = 32;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new MtvFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var bytes = MtvWriter.ToBytes(original);
    var restored = MtvReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mtv");
    try {
      var original = new MtvFile {
        Width = 3,
        Height = 2,
        PixelData = [
          255, 0, 0, 0, 255, 0, 0, 0, 255,
          128, 128, 128, 64, 64, 64, 32, 32, 32
        ]
      };

      var bytes = MtvWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = MtvReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
