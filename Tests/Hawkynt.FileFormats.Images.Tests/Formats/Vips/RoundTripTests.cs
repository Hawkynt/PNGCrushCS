using System;
using System.IO;
using FileFormat.Vips;

namespace FileFormat.Vips.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale() {
    var pixelData = new byte[2 * 2];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 63);

    var original = new VipsFile {
      Width = 2,
      Height = 2,
      Bands = 1,
      BandFormat = VipsBandFormat.UChar,
      PixelData = pixelData
    };

    var bytes = VipsWriter.ToBytes(original);
    var restored = VipsReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Bands, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb() {
    var pixelData = new byte[2 * 2 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var original = new VipsFile {
      Width = 2,
      Height = 2,
      Bands = 3,
      BandFormat = VipsBandFormat.UChar,
      PixelData = pixelData
    };

    var bytes = VipsWriter.ToBytes(original);
    var restored = VipsReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Bands, Is.EqualTo(3));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[3 * 2 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new VipsFile {
      Width = 3,
      Height = 2,
      Bands = 3,
      BandFormat = VipsBandFormat.UChar,
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".v");
    try {
      var bytes = VipsWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = VipsReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Bands, Is.EqualTo(original.Bands));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargerImage() {
    var width = 64;
    var height = 48;
    var bands = 3;
    var pixelData = new byte[width * height * bands];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new VipsFile {
      Width = width,
      Height = height,
      Bands = bands,
      BandFormat = VipsBandFormat.UChar,
      PixelData = pixelData
    };

    var bytes = VipsWriter.ToBytes(original);
    var restored = VipsReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.Bands, Is.EqualTo(bands));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Rgb() {
    var raw = new FileFormat.Core.RawImage {
      Width = 2,
      Height = 2,
      Format = FileFormat.Core.PixelFormat.Rgb24,
      PixelData = [0xFF, 0x00, 0x80, 0x40, 0x20, 0x10, 0xAA, 0xBB, 0xCC, 0x11, 0x22, 0x33]
    };

    var vips = VipsFile.FromRawImage(raw);
    var back = VipsFile.ToRawImage(vips);

    Assert.That(back.Width, Is.EqualTo(raw.Width));
    Assert.That(back.Height, Is.EqualTo(raw.Height));
    Assert.That(back.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Rgb24));
    Assert.That(back.PixelData, Is.EqualTo(raw.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Gray() {
    var raw = new FileFormat.Core.RawImage {
      Width = 2,
      Height = 2,
      Format = FileFormat.Core.PixelFormat.Gray8,
      PixelData = [0x00, 0x40, 0x80, 0xFF]
    };

    var vips = VipsFile.FromRawImage(raw);
    var back = VipsFile.ToRawImage(vips);

    Assert.That(back.Width, Is.EqualTo(raw.Width));
    Assert.That(back.Height, Is.EqualTo(raw.Height));
    Assert.That(back.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Gray8));
    Assert.That(back.PixelData, Is.EqualTo(raw.PixelData));
  }
}
