using System;
using System.IO;
using FileFormat.BigTiff;
using FileFormat.Core;

namespace FileFormat.BigTiff.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gray8() {
    var width = 4;
    var height = 3;
    var pixelData = new byte[width * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);
    var original = new BigTiffFile {
      Width = width, Height = height, SamplesPerPixel = 1, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack, PixelData = pixelData,
    };
    var bytes = BigTiffWriter.ToBytes(original);
    var restored = BigTiffReader.FromBytes(bytes);
    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.SamplesPerPixel, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb24() {
    var width = 4;
    var height = 3;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);
    var original = new BigTiffFile {
      Width = width, Height = height, SamplesPerPixel = 3, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricRgb, PixelData = pixelData,
    };
    var bytes = BigTiffWriter.ToBytes(original);
    var restored = BigTiffReader.FromBytes(bytes);
    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.SamplesPerPixel, Is.EqualTo(3));
    Assert.That(restored.PhotometricInterpretation, Is.EqualTo(BigTiffFile.PhotometricRgb));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var width = 8;
    var height = 6;
    var pixelData = new byte[width * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);
    var original = new BigTiffFile {
      Width = width, Height = height, SamplesPerPixel = 1, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack, PixelData = pixelData,
    };
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".btf");
    try {
      File.WriteAllBytes(tempPath, BigTiffWriter.ToBytes(original));
      var restored = BigTiffReader.FromFile(new FileInfo(tempPath));
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
  public void RoundTrip_AllZeros() {
    var original = new BigTiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack, PixelData = new byte[16],
    };
    var bytes = BigTiffWriter.ToBytes(original);
    var restored = BigTiffReader.FromBytes(bytes);
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Gray8() {
    var raw = new RawImage { Width = 4, Height = 3, Format = PixelFormat.Gray8, PixelData = new byte[12] };
    raw.PixelData[0] = 100;
    raw.PixelData[5] = 200;
    var file = BigTiffFile.FromRawImage(raw);
    var rawBack = BigTiffFile.ToRawImage(file);
    Assert.That(rawBack.Width, Is.EqualTo(4));
    Assert.That(rawBack.Height, Is.EqualTo(3));
    Assert.That(rawBack.Format, Is.EqualTo(PixelFormat.Gray8));
    Assert.That(rawBack.PixelData[0], Is.EqualTo(100));
    Assert.That(rawBack.PixelData[5], Is.EqualTo(200));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Rgb24() {
    var raw = new RawImage { Width = 2, Height = 2, Format = PixelFormat.Rgb24, PixelData = new byte[12] };
    raw.PixelData[0] = 255;
    var file = BigTiffFile.FromRawImage(raw);
    var rawBack = BigTiffFile.ToRawImage(file);
    Assert.That(rawBack.Width, Is.EqualTo(2));
    Assert.That(rawBack.Height, Is.EqualTo(2));
    Assert.That(rawBack.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(rawBack.PixelData[0], Is.EqualTo(255));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaStream() {
    var pixelData = new byte[12];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 21 % 256);
    var original = new BigTiffFile {
      Width = 4, Height = 3, SamplesPerPixel = 1, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack, PixelData = pixelData,
    };
    var bytes = BigTiffWriter.ToBytes(original);
    using var ms = new MemoryStream(bytes);
    var restored = BigTiffReader.FromStream(ms);
    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargerImage() {
    var width = 64;
    var height = 48;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);
    var original = new BigTiffFile {
      Width = width, Height = height, SamplesPerPixel = 3, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricRgb, PixelData = pixelData,
    };
    var bytes = BigTiffWriter.ToBytes(original);
    var restored = BigTiffReader.FromBytes(bytes);
    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.SamplesPerPixel, Is.EqualTo(3));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
