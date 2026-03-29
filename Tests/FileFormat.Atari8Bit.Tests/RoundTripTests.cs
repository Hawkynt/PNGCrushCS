using System;
using System.IO;
using FileFormat.Atari8Bit;

namespace FileFormat.Atari8Bit.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gr8() {
    var pixels = new byte[320 * 192];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 2);

    var original = new Atari8BitFile {
      Width = 320,
      Height = 192,
      Mode = Atari8BitMode.Gr8,
      PixelData = pixels,
    };

    var bytes = Atari8BitWriter.ToBytes(original);
    var restored = Atari8BitReader.FromBytes(bytes, Atari8BitMode.Gr8);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gr7() {
    var pixels = new byte[160 * 96];
    for (var y = 0; y < 96; ++y)
      for (var x = 0; x < 80; ++x) {
        var value = (byte)((y * 80 + x) % 4);
        pixels[y * 160 + x * 2] = value;
        pixels[y * 160 + x * 2 + 1] = value;
      }

    var original = new Atari8BitFile {
      Width = 160,
      Height = 96,
      Mode = Atari8BitMode.Gr7,
      PixelData = pixels,
    };

    var bytes = Atari8BitWriter.ToBytes(original);
    var restored = Atari8BitReader.FromBytes(bytes, Atari8BitMode.Gr7);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gr15() {
    var pixels = new byte[160 * 192];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 4);

    var original = new Atari8BitFile {
      Width = 160,
      Height = 192,
      Mode = Atari8BitMode.Gr15,
      PixelData = pixels,
    };

    var bytes = Atari8BitWriter.ToBytes(original);
    var restored = Atari8BitReader.FromBytes(bytes, Atari8BitMode.Gr15);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gr9() {
    var pixels = new byte[80 * 192];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 16);

    var original = new Atari8BitFile {
      Width = 80,
      Height = 192,
      Mode = Atari8BitMode.Gr9,
      PixelData = pixels,
    };

    var bytes = Atari8BitWriter.ToBytes(original);
    var restored = Atari8BitReader.FromBytes(bytes, Atari8BitMode.Gr9);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new Atari8BitFile {
      Width = 320,
      Height = 192,
      Mode = Atari8BitMode.Gr8,
      PixelData = new byte[320 * 192],
    };

    var bytes = Atari8BitWriter.ToBytes(original);
    var restored = Atari8BitReader.FromBytes(bytes, Atari8BitMode.Gr8);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixels = new byte[320 * 192];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 2);

    var original = new Atari8BitFile {
      Width = 320,
      Height = 192,
      Mode = Atari8BitMode.Gr8,
      PixelData = pixels,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gr8");
    try {
      var bytes = Atari8BitWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = Atari8BitReader.FromFile(new FileInfo(tempPath));

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

    var original = new Atari8BitFile {
      Width = 320,
      Height = 192,
      Mode = Atari8BitMode.Gr8,
      PixelData = pixels,
      Palette = Atari8BitFile.GetDefaultPalette(Atari8BitMode.Gr8),
    };

    var raw = Atari8BitFile.ToRawImage(original);
    var restored = Atari8BitFile.FromRawImage(raw);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Mode, Is.EqualTo(original.Mode));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
