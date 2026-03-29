using System;
using System.IO;
using FileFormat.Core;
using FileFormat.AtariAgp;

namespace FileFormat.AtariAgp.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gr8_AllZeros() {
    var original = new AtariAgpFile {
      Width = 320,
      Height = 192,
      Mode = AtariAgpMode.Graphics8,
      PixelData = new byte[320 * 192],
    };

    var bytes = AtariAgpWriter.ToBytes(original);
    var restored = AtariAgpReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(320));
    Assert.That(restored.Height, Is.EqualTo(192));
    Assert.That(restored.Mode, Is.EqualTo(AtariAgpMode.Graphics8));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gr8_PatternData() {
    var pixels = new byte[320 * 192];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 2);

    var original = new AtariAgpFile {
      Width = 320,
      Height = 192,
      Mode = AtariAgpMode.Graphics8,
      PixelData = pixels,
    };

    var bytes = AtariAgpWriter.ToBytes(original);
    var restored = AtariAgpReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gr7_PatternData() {
    var pixels = new byte[160 * 96];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 4);

    var original = new AtariAgpFile {
      Width = 160,
      Height = 96,
      Mode = AtariAgpMode.Graphics7,
      PixelData = pixels,
    };

    var bytes = AtariAgpWriter.ToBytes(original);
    var restored = AtariAgpReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(160));
    Assert.That(restored.Height, Is.EqualTo(96));
    Assert.That(restored.Mode, Is.EqualTo(AtariAgpMode.Graphics7));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gr8WithColors_PreservesColorBytes() {
    var pixels = new byte[320 * 192];
    pixels[0] = 1;

    var original = new AtariAgpFile {
      Width = 320,
      Height = 192,
      Mode = AtariAgpMode.Graphics8WithColors,
      PixelData = pixels,
      ForegroundColor = 0xAB,
      BackgroundColor = 0xCD,
    };

    var bytes = AtariAgpWriter.ToBytes(original);
    var restored = AtariAgpReader.FromBytes(bytes);

    Assert.That(restored.Mode, Is.EqualTo(AtariAgpMode.Graphics8WithColors));
    Assert.That(restored.ForegroundColor, Is.EqualTo(0xAB));
    Assert.That(restored.BackgroundColor, Is.EqualTo(0xCD));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gr8_ViaFile() {
    var pixels = new byte[320 * 192];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 2);

    var original = new AtariAgpFile {
      Width = 320,
      Height = 192,
      Mode = AtariAgpMode.Graphics8,
      PixelData = pixels,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".agp");
    try {
      var bytes = AtariAgpWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = AtariAgpReader.FromFile(new FileInfo(tempPath));

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
  public void RoundTrip_ViaRawImage_Gr8() {
    var pixels = new byte[320 * 192];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 2);

    var original = new AtariAgpFile {
      Width = 320,
      Height = 192,
      Mode = AtariAgpMode.Graphics8,
      PixelData = pixels,
      Palette = AtariAgpFile.DefaultGr8Palette[..],
    };

    var raw = AtariAgpFile.ToRawImage(original);
    var restored = AtariAgpFile.FromRawImage(raw);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Mode, Is.EqualTo(AtariAgpMode.Graphics8));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Gr7() {
    var pixels = new byte[160 * 96];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 4);

    var original = new AtariAgpFile {
      Width = 160,
      Height = 96,
      Mode = AtariAgpMode.Graphics7,
      PixelData = pixels,
      Palette = AtariAgpFile.DefaultGr7Palette[..],
    };

    var raw = AtariAgpFile.ToRawImage(original);
    var restored = AtariAgpFile.FromRawImage(raw);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Mode, Is.EqualTo(AtariAgpMode.Graphics7));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
