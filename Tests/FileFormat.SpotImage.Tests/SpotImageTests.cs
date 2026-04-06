using System;
using System.IO;
using FileFormat.Core;
using FileFormat.SpotImage;

namespace FileFormat.SpotImage.Tests;

[TestFixture]
public sealed class SpotImageReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SpotImageReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dat"));
    Assert.Throws<FileNotFoundException>(() => SpotImageReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SpotImageReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => SpotImageReader.FromBytes(new byte[10]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[16];
    data[0] = (byte)'X';
    Assert.Throws<InvalidDataException>(() => SpotImageReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_Parses() {
    var data = new byte[20];
    data[0] = (byte)'S'; data[1] = (byte)'P'; data[2] = (byte)'O'; data[3] = (byte)'T';
    data[4] = 4; data[5] = 0; // width = 4
    data[6] = 2; data[7] = 0; // height = 2
    data[8] = 8; data[9] = 0; // bpp = 8
    data[16] = 0xAB; // first pixel

    var result = SpotImageReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.BitsPerPixel, Is.EqualTo(8));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
  }
}

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale_PreservesData() {
    var pixelData = new byte[8];
    for (var i = 0; i < 8; ++i)
      pixelData[i] = (byte)(i * 30);

    var original = new SpotImageFile { Width = 4, Height = 2, BitsPerPixel = 8, PixelData = pixelData };

    var bytes = SpotImageWriter.ToBytes(original);
    var restored = SpotImageReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(8));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb_PreservesData() {
    var pixelData = new byte[24]; // 4x2x3
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 10 % 256);

    var original = new SpotImageFile { Width = 4, Height = 2, BitsPerPixel = 24, PixelData = pixelData };

    var bytes = SpotImageWriter.ToBytes(original);
    var restored = SpotImageReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Gray() {
    var pixelData = new byte[] { 10, 20, 30, 40, 50, 60, 70, 80 };
    var original = new SpotImageFile { Width = 4, Height = 2, BitsPerPixel = 8, PixelData = pixelData };

    var raw = SpotImageFile.ToRawImage(original);
    var restored = SpotImageFile.FromRawImage(raw);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}

