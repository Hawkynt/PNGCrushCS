using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Mrc;

namespace FileFormat.Mrc.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SmallGrayscale() {
    var pixels = new byte[4 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 21);

    var original = new MrcFile {
      Width = 4,
      Height = 3,
      Sections = 1,
      Mode = 0,
      PixelData = pixels,
    };

    var bytes = MrcWriter.ToBytes(original);
    var restored = MrcReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Sections, Is.EqualTo(original.Sections));
    Assert.That(restored.Mode, Is.EqualTo(original.Mode));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new MrcFile {
      Width = 8,
      Height = 8,
      Sections = 1,
      Mode = 0,
      PixelData = new byte[64],
    };

    var bytes = MrcWriter.ToBytes(original);
    var restored = MrcReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllMax() {
    var pixels = new byte[16];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = 0xFF;

    var original = new MrcFile {
      Width = 4,
      Height = 4,
      Sections = 1,
      Mode = 0,
      PixelData = pixels,
    };

    var bytes = MrcWriter.ToBytes(original);
    var restored = MrcReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixels = new byte[10 * 10];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 7 % 256);

    var original = new MrcFile {
      Width = 10,
      Height = 10,
      Sections = 1,
      Mode = 0,
      PixelData = pixels,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mrc");
    try {
      var bytes = MrcWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = MrcReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Sections, Is.EqualTo(original.Sections));
      Assert.That(restored.Mode, Is.EqualTo(original.Mode));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithExtendedHeader() {
    var extHeader = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE };
    var pixels = new byte[3 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i + 100);

    var original = new MrcFile {
      Width = 3,
      Height = 3,
      Sections = 1,
      Mode = 0,
      ExtendedHeader = extHeader,
      PixelData = pixels,
    };

    var bytes = MrcWriter.ToBytes(original);
    var restored = MrcReader.FromBytes(bytes);

    Assert.That(restored.ExtendedHeaderSize, Is.EqualTo(extHeader.Length));
    Assert.That(restored.ExtendedHeader, Is.EqualTo(extHeader));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var pixels = new byte[5 * 4];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 13 % 256);

    var original = new MrcFile {
      Width = 5,
      Height = 4,
      Sections = 1,
      Mode = 0,
      PixelData = pixels,
    };

    var raw = MrcFile.ToRawImage(original);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));
    Assert.That(raw.Width, Is.EqualTo(5));
    Assert.That(raw.Height, Is.EqualTo(4));

    var restored = MrcFile.FromRawImage(raw);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Mode, Is.EqualTo(0));
    Assert.That(restored.Sections, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargerImage() {
    var pixels = new byte[64 * 64];
    var rng = new Random(42);
    rng.NextBytes(pixels);

    var original = new MrcFile {
      Width = 64,
      Height = 64,
      Sections = 1,
      Mode = 0,
      PixelData = pixels,
    };

    var bytes = MrcWriter.ToBytes(original);
    var restored = MrcReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(64));
    Assert.That(restored.Height, Is.EqualTo(64));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
