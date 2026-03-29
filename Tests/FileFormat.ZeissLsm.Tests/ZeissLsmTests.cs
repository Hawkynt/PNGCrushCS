using System;
using System.IO;
using FileFormat.Core;
using FileFormat.ZeissLsm;

namespace FileFormat.ZeissLsm.Tests;

[TestFixture]
public sealed class ZeissLsmReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ZeissLsmReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".lsm"));
    Assert.Throws<FileNotFoundException>(() => ZeissLsmReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ZeissLsmReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => ZeissLsmReader.FromBytes(new byte[4]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[8];
    data[0] = 0x49; data[1] = 0x49;
    data[2] = 0xFF; data[3] = 0xFF; // wrong magic
    Assert.Throws<InvalidDataException>(() => ZeissLsmReader.FromBytes(data));
  }
}

[TestFixture]
public sealed class ZeissLsmWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ZeissLsmWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ProducesValidTiffHeader() {
    var file = new ZeissLsmFile { Width = 4, Height = 4, Channels = 1, PixelData = new byte[16] };

    var bytes = ZeissLsmWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x49));
    Assert.That(bytes[1], Is.EqualTo(0x49));
    Assert.That(bytes[2], Is.EqualTo(42));
    Assert.That(bytes[3], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsPixelData() {
    var pixelData = new byte[] { 10, 20, 30, 40 };
    var file = new ZeissLsmFile { Width = 2, Height = 2, Channels = 1, PixelData = pixelData };

    var bytes = ZeissLsmWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThan(4));
  }
}

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale_PreservesData() {
    var pixelData = new byte[16];
    for (var i = 0; i < 16; ++i)
      pixelData[i] = (byte)(i * 15);

    var original = new ZeissLsmFile { Width = 4, Height = 4, Channels = 1, PixelData = pixelData };

    var bytes = ZeissLsmWriter.ToBytes(original);
    var restored = ZeissLsmReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.Channels, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb_PreservesData() {
    var pixelData = new byte[48]; // 4x4x3
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 5 % 256);

    var original = new ZeissLsmFile { Width = 4, Height = 4, Channels = 3, PixelData = pixelData };

    var bytes = ZeissLsmWriter.ToBytes(original);
    var restored = ZeissLsmReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.Channels, Is.EqualTo(3));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Gray() {
    var pixelData = new byte[16];
    pixelData[0] = 128;
    var original = new ZeissLsmFile { Width = 4, Height = 4, Channels = 1, PixelData = pixelData };

    var raw = ZeissLsmFile.ToRawImage(original);
    var restored = ZeissLsmFile.FromRawImage(raw);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void ZeissLsmFile_Defaults() {
    var file = new ZeissLsmFile();
    Assert.That(file.Width, Is.EqualTo(0));
    Assert.That(file.Height, Is.EqualTo(0));
    Assert.That(file.Channels, Is.EqualTo(1));
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void ZeissLsmFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ZeissLsmFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ZeissLsmFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ZeissLsmFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ZeissLsmFile_FromRawImage_WrongFormat_Throws() {
    var raw = new RawImage {
      Width = 4, Height = 4,
      Format = PixelFormat.Rgba32,
      PixelData = new byte[64],
    };
    Assert.Throws<ArgumentException>(() => ZeissLsmFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ZeissLsmFile_ToRawImage_Gray_ReturnsGray8() {
    var file = new ZeissLsmFile { Width = 2, Height = 2, Channels = 1, PixelData = new byte[4] };
    var raw = ZeissLsmFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));
  }

  [Test]
  [Category("Unit")]
  public void ZeissLsmFile_ToRawImage_Rgb_ReturnsRgb24() {
    var file = new ZeissLsmFile { Width = 2, Height = 2, Channels = 3, PixelData = new byte[12] };
    var raw = ZeissLsmFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }
}
