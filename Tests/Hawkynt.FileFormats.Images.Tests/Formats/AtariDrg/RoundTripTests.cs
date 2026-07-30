using System;
using System.IO;
using FileFormat.Core;
using FileFormat.AtariDrg;

namespace FileFormat.AtariDrg.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new AtariDrgFile {
      PixelData = new byte[160 * 192]
    };

    var bytes = AtariDrgWriter.ToBytes(original);
    var restored = AtariDrgReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(160));
    Assert.That(restored.Height, Is.EqualTo(192));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PatternData() {
    var pixels = new byte[160 * 192];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 4);

    var original = new AtariDrgFile { PixelData = pixels };

    var bytes = AtariDrgWriter.ToBytes(original);
    var restored = AtariDrgReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllMaxValues() {
    var pixels = new byte[160 * 192];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = 3;

    var original = new AtariDrgFile { PixelData = pixels };

    var bytes = AtariDrgWriter.ToBytes(original);
    var restored = AtariDrgReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixels = new byte[160 * 192];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 4);

    var original = new AtariDrgFile { PixelData = pixels };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".drg");
    try {
      var bytes = AtariDrgWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = AtariDrgReader.FromFile(new FileInfo(tempPath));

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
  public void RoundTrip_ViaRawImage() {
    var pixels = new byte[160 * 192];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 4);

    var original = new AtariDrgFile {
      PixelData = pixels,
      Palette = AtariDrgFile.DefaultPalette[..],
    };

    var raw = AtariDrgFile.ToRawImage(original);
    var restored = AtariDrgFile.FromRawImage(raw);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_AllZeros() {
    var original = new AtariDrgFile {
      PixelData = new byte[160 * 192],
      Palette = AtariDrgFile.DefaultPalette[..],
    };

    var raw = AtariDrgFile.ToRawImage(original);
    var restored = AtariDrgFile.FromRawImage(raw);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SpecificByte_Encoding() {
    var pixels = new byte[160 * 192];
    pixels[0] = 2;
    pixels[1] = 1;
    pixels[2] = 3;
    pixels[3] = 0;

    var original = new AtariDrgFile { PixelData = pixels };

    var bytes = AtariDrgWriter.ToBytes(original);
    Assert.That(bytes[0], Is.EqualTo(0b10011100));

    var restored = AtariDrgReader.FromBytes(bytes);
    Assert.That(restored.PixelData[0], Is.EqualTo(2));
    Assert.That(restored.PixelData[1], Is.EqualTo(1));
    Assert.That(restored.PixelData[2], Is.EqualTo(3));
    Assert.That(restored.PixelData[3], Is.EqualTo(0));
  }
}
