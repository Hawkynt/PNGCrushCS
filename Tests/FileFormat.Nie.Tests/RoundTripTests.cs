using System;
using System.IO;
using FileFormat.Nie;
using FileFormat.Core;

namespace FileFormat.Nie.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Bgra8_PreservesPixelData() {
    var pixels = new byte[3 * 2 * 4];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 7);

    var original = new NieFile {
      Width = 3, Height = 2, PixelConfig = NiePixelConfig.Bgra8, PixelData = pixels
    };

    var bytes = NieWriter.ToBytes(original);
    var restored = NieReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(3));
      Assert.That(restored.Height, Is.EqualTo(2));
      Assert.That(restored.PixelConfig, Is.EqualTo(NiePixelConfig.Bgra8));
      Assert.That(restored.PixelData, Has.Length.EqualTo(pixels.Length));
    });
    for (var i = 0; i < pixels.Length; ++i)
      Assert.That(restored.PixelData[i], Is.EqualTo(pixels[i]));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Bgra16_PreservesPixelData() {
    var pixels = new byte[2 * 2 * 8];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 3);

    var original = new NieFile {
      Width = 2, Height = 2, PixelConfig = NiePixelConfig.Bgra16, PixelData = pixels
    };

    var bytes = NieWriter.ToBytes(original);
    var restored = NieReader.FromBytes(bytes);

    Assert.That(restored.PixelConfig, Is.EqualTo(NiePixelConfig.Bgra16));
    for (var i = 0; i < pixels.Length; ++i)
      Assert.That(restored.PixelData[i], Is.EqualTo(pixels[i]));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BgraPremul8_PreservesConfig() {
    var pixels = new byte[2 * 2 * 4];
    var original = new NieFile {
      Width = 2, Height = 2, PixelConfig = NiePixelConfig.BgraPremul8, PixelData = pixels
    };

    var bytes = NieWriter.ToBytes(original);
    var restored = NieReader.FromBytes(bytes);
    Assert.That(restored.PixelConfig, Is.EqualTo(NiePixelConfig.BgraPremul8));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BgraPremul16_PreservesConfig() {
    var pixels = new byte[2 * 2 * 8];
    var original = new NieFile {
      Width = 2, Height = 2, PixelConfig = NiePixelConfig.BgraPremul16, PixelData = pixels
    };

    var bytes = NieWriter.ToBytes(original);
    var restored = NieReader.FromBytes(bytes);
    Assert.That(restored.PixelConfig, Is.EqualTo(NiePixelConfig.BgraPremul16));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixels = new byte[2 * 2 * 4];
    pixels[0] = 0xFF;
    pixels[7] = 0xAB;
    var original = new NieFile {
      Width = 2, Height = 2, PixelConfig = NiePixelConfig.Bgra8, PixelData = pixels
    };

    var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".nie");
    try {
      File.WriteAllBytes(tmp, NieWriter.ToBytes(original));
      var restored = NieReader.FromFile(new FileInfo(tmp));
      Assert.Multiple(() => {
        Assert.That(restored.Width, Is.EqualTo(2));
        Assert.That(restored.Height, Is.EqualTo(2));
        Assert.That(restored.PixelData[0], Is.EqualTo(0xFF));
        Assert.That(restored.PixelData[7], Is.EqualTo(0xAB));
      });
    } finally {
      try { File.Delete(tmp); } catch { /* best effort */ }
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Bgra8() {
    var pixels = new byte[2 * 2 * 4];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 11);

    var original = new NieFile {
      Width = 2, Height = 2, PixelConfig = NiePixelConfig.Bgra8, PixelData = pixels
    };

    var raw = NieFile.ToRawImage(original);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Bgra32));

    var restored = NieFile.FromRawImage(raw);
    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(2));
      Assert.That(restored.Height, Is.EqualTo(2));
      Assert.That(restored.PixelConfig, Is.EqualTo(NiePixelConfig.Bgra8));
      Assert.That(restored.PixelData, Has.Length.EqualTo(16));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Bgra16() {
    var pixels = new byte[2 * 2 * 8];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 5);

    var original = new NieFile {
      Width = 2, Height = 2, PixelConfig = NiePixelConfig.Bgra16, PixelData = pixels
    };

    var raw = NieFile.ToRawImage(original);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgba64));

    var restored = NieFile.FromRawImage(raw);
    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(2));
      Assert.That(restored.Height, Is.EqualTo(2));
      Assert.That(restored.PixelConfig, Is.EqualTo(NiePixelConfig.Bgra16));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new NieFile {
      Width = 4, Height = 4, PixelConfig = NiePixelConfig.Bgra8,
      PixelData = new byte[64]
    };

    var bytes = NieWriter.ToBytes(original);
    var restored = NieReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.All.EqualTo(0));
  }
}
