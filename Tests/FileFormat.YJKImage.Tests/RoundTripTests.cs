using System;
using System.IO;
using FileFormat.YJKImage;

namespace FileFormat.YJKImage.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_DataPreserved() {
    var pixels = new byte[54272];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 13 % 256);

    var original = new YJKImageFile { PixelData = pixels };
    var bytes = YJKImageWriter.ToBytes(original);
    var restored = YJKImageReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new YJKImageFile { PixelData = new byte[54272] };

    var bytes = YJKImageWriter.ToBytes(original);
    var restored = YJKImageReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixels = new byte[54272];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 7 % 256);

    var original = new YJKImageFile { PixelData = pixels };
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".yjk");
    try {
      var bytes = YJKImageWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = YJKImageReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
