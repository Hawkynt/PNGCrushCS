using System;
using System.IO;
using FileFormat.Core;
using FileFormat.GodPaint;

namespace FileFormat.GodPaint.Tests;

[TestFixture]
public sealed class GodPaintTests {

  private static GodPaintFile _Sample() {
    var pixels = new byte[GodPaintFile.PixelDataSize];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 31 % 256);

    return new() { PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void FileSize_IsHeaderPlusPixels()
    => Assert.That(GodPaintFile.ExpectedFileSize, Is.EqualTo(GodPaintFile.HeaderSize + GodPaintFile.PixelDataSize));

  [Test]
  [Category("Unit")]
  public void ToBytes_StoresDimensionsBigEndian() {
    var bytes = GodPaintWriter.ToBytes(_Sample());

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(GodPaintFile.ExpectedFileSize));
      Assert.That(bytes[GodPaintFile.DimensionsOffset], Is.EqualTo(320 >> 8));
      Assert.That(bytes[GodPaintFile.DimensionsOffset + 1], Is.EqualTo(320 & 0xFF));
      Assert.That(bytes[GodPaintFile.DimensionsOffset + 2], Is.EqualTo(240 >> 8));
      Assert.That(bytes[GodPaintFile.DimensionsOffset + 3], Is.EqualTo(240 & 0xFF));
    });
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesEveryPixel() {
    var original = _Sample();
    var restored = GodPaintReader.FromBytes(GodPaintWriter.ToBytes(original));

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAnyOtherLength()
    => Assert.Throws<InvalidDataException>(() => GodPaintReader.FromBytes(new byte[GodPaintFile.PixelDataSize]));

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesTheScreenResolution() {
    var raw = GodPaintFile.ToRawImage(_Sample());

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(320));
      Assert.That(raw.Height, Is.EqualTo(240));
    });
  }
}
