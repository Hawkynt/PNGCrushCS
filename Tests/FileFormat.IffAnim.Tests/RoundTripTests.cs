using System;
using System.IO;
using FileFormat.Core;
using FileFormat.IffAnim;

namespace FileFormat.IffAnim.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SingleFrame() {
    var pixels = new byte[2 * 2 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 21 % 256);

    var original = new IffAnimFile {
      Width = 2,
      Height = 2,
      PixelData = pixels
    };

    var bytes = IffAnimWriter.ToBytes(original);
    var restored = IffAnimReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData.Length, Is.EqualTo(original.PixelData.Length));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixels = new byte[4 * 3 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 7 % 256);

    var original = new IffAnimFile {
      Width = 4,
      Height = 3,
      PixelData = pixels
    };

    var bytes = IffAnimWriter.ToBytes(original);
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".anim");
    try {
      File.WriteAllBytes(tempPath, bytes);
      var restored = IffAnimReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData.Length, Is.EqualTo(original.PixelData.Length));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargerImage() {
    var width = 16;
    var height = 8;
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 13 % 256);

    var original = new IffAnimFile {
      Width = width,
      Height = height,
      PixelData = pixels
    };

    var bytes = IffAnimWriter.ToBytes(original);
    var restored = IffAnimReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData.Length, Is.EqualTo(pixels.Length));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var rawImage = new RawImage {
      Width = 4,
      Height = 2,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[4 * 2 * 3],
    };
    for (var i = 0; i < rawImage.PixelData.Length; ++i)
      rawImage.PixelData[i] = (byte)(i * 11 % 256);

    var animFile = IffAnimFile.FromRawImage(rawImage);
    var bytes = IffAnimWriter.ToBytes(animFile);
    var restored = IffAnimReader.FromBytes(bytes);
    var restoredRaw = IffAnimFile.ToRawImage(restored);

    Assert.That(restoredRaw.Width, Is.EqualTo(rawImage.Width));
    Assert.That(restoredRaw.Height, Is.EqualTo(rawImage.Height));
    Assert.That(restoredRaw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new IffAnimFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = IffAnimWriter.ToBytes(original);
    var restored = IffAnimReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(2));
    Assert.That(restored.Height, Is.EqualTo(2));
  }
}
