using System;
using System.IO;
using FileFormat.Phm;
using FileFormat.Core;

namespace FileFormat.Phm.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb_PreservesPixelData() {
    var pixelData = new Half[3 * 2 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (Half)(i * 0.01f);

    var original = new PhmFile {
      Width = 3, Height = 2, ColorMode = PhmColorMode.Rgb,
      Scale = 1.0f, IsLittleEndian = true, PixelData = pixelData
    };

    var bytes = PhmWriter.ToBytes(original);
    var restored = PhmReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.ColorMode, Is.EqualTo(PhmColorMode.Rgb));
      Assert.That(restored.Scale, Is.EqualTo(1.0f));
      Assert.That(restored.IsLittleEndian, Is.True);
      Assert.That(restored.PixelData, Has.Length.EqualTo(original.PixelData.Length));
    });
    for (var i = 0; i < original.PixelData.Length; ++i)
      Assert.That(restored.PixelData[i], Is.EqualTo(original.PixelData[i]));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale_PreservesPixelData() {
    var pixelData = new Half[4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (Half)(i * 0.05f);

    var original = new PhmFile {
      Width = 4, Height = 3, ColorMode = PhmColorMode.Grayscale,
      Scale = 1.0f, IsLittleEndian = true, PixelData = pixelData
    };

    var bytes = PhmWriter.ToBytes(original);
    var restored = PhmReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Has.Length.EqualTo(original.PixelData.Length));
    for (var i = 0; i < original.PixelData.Length; ++i)
      Assert.That(restored.PixelData[i], Is.EqualTo(original.PixelData[i]));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BigEndian_PreservesPixelData() {
    var pixelData = new Half[] { (Half)1.0f, (Half)0.5f, (Half)0.25f, (Half)0.75f, (Half)0.125f, (Half)0.875f };

    var original = new PhmFile {
      Width = 2, Height = 1, ColorMode = PhmColorMode.Rgb,
      Scale = 2.0f, IsLittleEndian = false, PixelData = pixelData
    };

    var bytes = PhmWriter.ToBytes(original);
    var restored = PhmReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.IsLittleEndian, Is.False);
      Assert.That(restored.Scale, Is.EqualTo(2.0f));
    });
    for (var i = 0; i < pixelData.Length; ++i)
      Assert.That(restored.PixelData[i], Is.EqualTo(pixelData[i]));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ScalePreserved() {
    var original = new PhmFile {
      Width = 1, Height = 1, ColorMode = PhmColorMode.Grayscale,
      Scale = 0.5f, IsLittleEndian = true, PixelData = [(Half)42.0f]
    };

    var bytes = PhmWriter.ToBytes(original);
    var restored = PhmReader.FromBytes(bytes);
    Assert.That(restored.Scale, Is.EqualTo(0.5f));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = new PhmFile {
      Width = 2, Height = 2, ColorMode = PhmColorMode.Grayscale,
      Scale = 1.0f, IsLittleEndian = true,
      PixelData = [(Half)0.1f, (Half)0.2f, (Half)0.3f, (Half)0.4f]
    };

    var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".phm");
    try {
      File.WriteAllBytes(tmp, PhmWriter.ToBytes(original));
      var restored = PhmReader.FromFile(new FileInfo(tmp));
      Assert.Multiple(() => {
        Assert.That(restored.Width, Is.EqualTo(2));
        Assert.That(restored.Height, Is.EqualTo(2));
        Assert.That(restored.PixelData, Has.Length.EqualTo(4));
      });
    } finally {
      try { File.Delete(tmp); } catch { /* best effort */ }
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Grayscale() {
    var original = new PhmFile {
      Width = 2, Height = 2, ColorMode = PhmColorMode.Grayscale,
      Scale = 1.0f, IsLittleEndian = true,
      PixelData = [(Half)0.0f, (Half)0.33f, (Half)0.66f, (Half)1.0f]
    };

    var raw = PhmFile.ToRawImage(original);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray16));

    var restored = PhmFile.FromRawImage(raw);
    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(2));
      Assert.That(restored.Height, Is.EqualTo(2));
      Assert.That(restored.ColorMode, Is.EqualTo(PhmColorMode.Grayscale));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Rgb() {
    var pixelData = new Half[2 * 2 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (Half)(i / (float)pixelData.Length);

    var original = new PhmFile {
      Width = 2, Height = 2, ColorMode = PhmColorMode.Rgb,
      Scale = 1.0f, IsLittleEndian = true, PixelData = pixelData
    };

    var raw = PhmFile.ToRawImage(original);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb48));

    var restored = PhmFile.FromRawImage(raw);
    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(2));
      Assert.That(restored.Height, Is.EqualTo(2));
      Assert.That(restored.ColorMode, Is.EqualTo(PhmColorMode.Rgb));
      Assert.That(restored.PixelData, Has.Length.EqualTo(12));
    });
  }
}
