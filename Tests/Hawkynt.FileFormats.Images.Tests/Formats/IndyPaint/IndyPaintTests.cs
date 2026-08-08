using System;
using System.IO;
using FileFormat.Core;
using FileFormat.IndyPaint;

namespace FileFormat.IndyPaint.Tests;

[TestFixture]
public sealed class IndyPaintTests {

  private static IndyPaintFile _Sample() {
    var pixels = new byte[IndyPaintFile.PixelDataSize];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 31 % 256);

    // The size comes from the header now, so a sample that states only its pixels states nothing:
    // it used to be fixed at 320 by 240 and no longer is.
    return new() { Width = IndyPaintFile.DefaultWidth, Height = IndyPaintFile.DefaultHeight, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void FileSize_IsHeaderPlusPixels()
    => Assert.That(IndyPaintFile.ExpectedFileSize, Is.EqualTo(IndyPaintFile.HeaderSize + IndyPaintFile.PixelDataSize));

  [Test]
  [Category("Unit")]
  public void ToBytes_StoresDimensionsBigEndian() {
    var bytes = IndyPaintWriter.ToBytes(_Sample());

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(IndyPaintFile.ExpectedFileSize));
      Assert.That(bytes[..IndyPaintFile.Signature.Length], Is.EqualTo(IndyPaintFile.Signature.ToArray()), "Indy signature");
      Assert.That(bytes[IndyPaintFile.DimensionsOffset], Is.EqualTo(320 >> 8));
      Assert.That(bytes[IndyPaintFile.DimensionsOffset + 1], Is.EqualTo(320 & 0xFF));
      Assert.That(bytes[IndyPaintFile.DimensionsOffset + 2], Is.EqualTo(240 >> 8));
      Assert.That(bytes[IndyPaintFile.DimensionsOffset + 3], Is.EqualTo(240 & 0xFF));
    });
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesEveryPixel() {
    var original = _Sample();
    var restored = IndyPaintReader.FromBytes(IndyPaintWriter.ToBytes(original));

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAnyOtherLength()
    => Assert.Throws<InvalidDataException>(() => IndyPaintReader.FromBytes(new byte[IndyPaintFile.PixelDataSize]));

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesTheScreenResolution() {
    var raw = IndyPaintFile.ToRawImage(_Sample());

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(320));
      Assert.That(raw.Height, Is.EqualTo(240));
    });
  }
}
