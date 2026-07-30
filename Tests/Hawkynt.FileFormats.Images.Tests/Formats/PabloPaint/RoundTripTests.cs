using System;
using System.IO;
using FileFormat.PabloPaint;
using FileFormat.Core;

namespace FileFormat.PabloPaint.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_PreservesPixelData() {
    var pixelData = new byte[32000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new PabloPaintFile { PixelData = pixelData };
    var bytes = PabloPaintWriter.ToBytes(original);
    var restored = PabloPaintReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(640));
      Assert.That(restored.Height, Is.EqualTo(400));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new PabloPaintFile { PixelData = new byte[32000] };
    var bytes = PabloPaintWriter.ToBytes(original);
    var restored = PabloPaintReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllOnes() {
    var pixelData = new byte[32000];
    Array.Fill(pixelData, (byte)0xFF);

    var original = new PabloPaintFile { PixelData = pixelData };
    var bytes = PabloPaintWriter.ToBytes(original);
    var restored = PabloPaintReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[32000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new PabloPaintFile { PixelData = pixelData };
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pa3");
    try {
      File.WriteAllBytes(tempPath, PabloPaintWriter.ToBytes(original));
      var restored = PabloPaintReader.FromFile(new FileInfo(tempPath));

      Assert.Multiple(() => {
        Assert.That(restored.Width, Is.EqualTo(640));
        Assert.That(restored.Height, Is.EqualTo(400));
        Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      });
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var rowStride = 640 / 8;
    var rawPixelData = new byte[rowStride * 400];
    rawPixelData[0] = 0x80;
    rawPixelData[rowStride] = 0x40;

    var raw = new RawImage {
      Width = 640,
      Height = 400,
      Format = PixelFormat.Indexed1,
      PixelData = rawPixelData,
      Palette = [255, 255, 255, 0, 0, 0],
      PaletteCount = 2,
    };

    var file = PabloPaintFile.FromRawImage(raw);
    var rawBack = PabloPaintFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(rawBack.Width, Is.EqualTo(640));
      Assert.That(rawBack.Height, Is.EqualTo(400));
      Assert.That(rawBack.Format, Is.EqualTo(PixelFormat.Indexed1));
      Assert.That(rawBack.PaletteCount, Is.EqualTo(2));
      Assert.That(rawBack.PixelData[0], Is.EqualTo(0x80));
      Assert.That(rawBack.PixelData[rowStride], Is.EqualTo(0x40));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_AllZeros() {
    var rawPixelData = new byte[640 / 8 * 400];
    var raw = new RawImage {
      Width = 640, Height = 400, Format = PixelFormat.Indexed1,
      PixelData = rawPixelData, Palette = [255, 255, 255, 0, 0, 0], PaletteCount = 2,
    };

    var file = PabloPaintFile.FromRawImage(raw);
    var rawBack = PabloPaintFile.ToRawImage(file);

    Assert.That(rawBack.PixelData, Is.EqualTo(rawPixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_AllPixelsSet() {
    var rawPixelData = new byte[640 / 8 * 400];
    Array.Fill(rawPixelData, (byte)0xFF);
    var raw = new RawImage {
      Width = 640, Height = 400, Format = PixelFormat.Indexed1,
      PixelData = rawPixelData, Palette = [255, 255, 255, 0, 0, 0], PaletteCount = 2,
    };

    var file = PabloPaintFile.FromRawImage(raw);
    var rawBack = PabloPaintFile.ToRawImage(file);

    Assert.That(rawBack.PixelData, Is.EqualTo(rawPixelData));
  }
}
