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
public sealed class SpotImageWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SpotImageWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasSpotMagic() {
    var file = new SpotImageFile { Width = 4, Height = 2, BitsPerPixel = 8, PixelData = new byte[8] };
    var bytes = SpotImageWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'S'));
    Assert.That(bytes[1], Is.EqualTo((byte)'P'));
    Assert.That(bytes[2], Is.EqualTo((byte)'O'));
    Assert.That(bytes[3], Is.EqualTo((byte)'T'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectSize() {
    var file = new SpotImageFile { Width = 4, Height = 2, BitsPerPixel = 8, PixelData = new byte[8] };
    var bytes = SpotImageWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(24)); // 16 header + 8 pixels
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

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void SpotImageFile_Defaults() {
    var file = new SpotImageFile();
    Assert.That(file.Width, Is.EqualTo(0));
    Assert.That(file.Height, Is.EqualTo(0));
    Assert.That(file.BitsPerPixel, Is.EqualTo(8));
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void SpotImageFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SpotImageFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void SpotImageFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SpotImageFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void SpotImageFile_FromRawImage_WrongFormat_Throws() {
    var raw = new RawImage {
      Width = 4, Height = 2,
      Format = PixelFormat.Rgba32,
      PixelData = new byte[32],
    };
    Assert.Throws<ArgumentException>(() => SpotImageFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void SpotImageFile_ToRawImage_Gray_ReturnsGray8() {
    var file = new SpotImageFile { Width = 2, Height = 2, BitsPerPixel = 8, PixelData = new byte[4] };
    var raw = SpotImageFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));
  }

  [Test]
  [Category("Unit")]
  public void SpotImageFile_ToRawImage_Rgb_ReturnsRgb24() {
    var file = new SpotImageFile { Width = 2, Height = 2, BitsPerPixel = 24, PixelData = new byte[12] };
    var raw = SpotImageFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }
}
