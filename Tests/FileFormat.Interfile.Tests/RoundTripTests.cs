using System;
using System.IO;
using FileFormat.Interfile;

namespace FileFormat.Interfile.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale8() {
    var pixelData = new byte[4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var original = new InterfileFile {
      Width = 4,
      Height = 3,
      BytesPerPixel = 1,
      NumberFormat = "unsigned integer",
      PixelData = pixelData
    };

    var bytes = InterfileWriter.ToBytes(original);
    var restored = InterfileReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.BytesPerPixel, Is.EqualTo(1));
    Assert.That(restored.NumberFormat, Is.EqualTo("unsigned integer"));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb24() {
    var pixelData = new byte[3 * 2 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new InterfileFile {
      Width = 3,
      Height = 2,
      BytesPerPixel = 3,
      NumberFormat = "unsigned integer",
      PixelData = pixelData
    };

    var bytes = InterfileWriter.ToBytes(original);
    var restored = InterfileReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(3));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.BytesPerPixel, Is.EqualTo(3));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[5 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 3 % 256);

    var original = new InterfileFile {
      Width = 5,
      Height = 4,
      BytesPerPixel = 1,
      NumberFormat = "unsigned integer",
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hv");
    try {
      var bytes = InterfileWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = InterfileReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.BytesPerPixel, Is.EqualTo(original.BytesPerPixel));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Gray8() {
    var raw = new FileFormat.Core.RawImage {
      Width = 3,
      Height = 2,
      Format = FileFormat.Core.PixelFormat.Gray8,
      PixelData = [10, 20, 30, 40, 50, 60]
    };

    var interfile = InterfileFile.FromRawImage(raw);
    var bytes = InterfileWriter.ToBytes(interfile);
    var restored = InterfileReader.FromBytes(bytes);
    var restoredRaw = InterfileFile.ToRawImage(restored);

    Assert.That(restoredRaw.Width, Is.EqualTo(3));
    Assert.That(restoredRaw.Height, Is.EqualTo(2));
    Assert.That(restoredRaw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Gray8));
    Assert.That(restoredRaw.PixelData, Is.EqualTo(raw.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Rgb24() {
    var pixels = new byte[2 * 2 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 5);

    var raw = new FileFormat.Core.RawImage {
      Width = 2,
      Height = 2,
      Format = FileFormat.Core.PixelFormat.Rgb24,
      PixelData = pixels
    };

    var interfile = InterfileFile.FromRawImage(raw);
    var bytes = InterfileWriter.ToBytes(interfile);
    var restored = InterfileReader.FromBytes(bytes);
    var restoredRaw = InterfileFile.ToRawImage(restored);

    Assert.That(restoredRaw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Rgb24));
    Assert.That(restoredRaw.PixelData, Is.EqualTo(raw.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new InterfileFile {
      Width = 4,
      Height = 4,
      BytesPerPixel = 1,
      PixelData = new byte[16]
    };

    var bytes = InterfileWriter.ToBytes(original);
    var restored = InterfileReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargerImage() {
    var width = 64;
    var height = 32;
    var pixelData = new byte[width * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new InterfileFile {
      Width = width,
      Height = height,
      BytesPerPixel = 1,
      PixelData = pixelData
    };

    var bytes = InterfileWriter.ToBytes(original);
    var restored = InterfileReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_NumberFormatPreserved() {
    var original = new InterfileFile {
      Width = 2,
      Height = 2,
      BytesPerPixel = 2,
      NumberFormat = "signed integer",
      PixelData = new byte[8]
    };

    var bytes = InterfileWriter.ToBytes(original);
    var restored = InterfileReader.FromBytes(bytes);

    Assert.That(restored.NumberFormat, Is.EqualTo("signed integer"));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaStream() {
    var pixelData = new byte[3 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new InterfileFile {
      Width = 3,
      Height = 3,
      BytesPerPixel = 1,
      PixelData = pixelData
    };

    var bytes = InterfileWriter.ToBytes(original);
    using var ms = new MemoryStream(bytes);
    var restored = InterfileReader.FromStream(ms);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
