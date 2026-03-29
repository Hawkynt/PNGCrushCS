using System;
using System.IO;
using FileFormat.MegaPaint;

namespace FileFormat.MegaPaint.Tests;

[TestFixture]
public sealed class MegaPaintReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MegaPaintReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MegaPaintReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bld"));
    Assert.Throws<FileNotFoundException>(() => MegaPaintReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MegaPaintReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[7];
    Assert.Throws<InvalidDataException>(() => MegaPaintReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroDimensions_ThrowsInvalidDataException() {
    var data = new byte[8]; // width=0, height=0
    Assert.Throws<InvalidDataException>(() => MegaPaintReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InsufficientPixelData_ThrowsInvalidDataException() {
    var data = new byte[8];
    data[0] = 0x00; data[1] = 0x10; // width=16 big endian
    data[2] = 0x00; data[3] = 0x10; // height=16 big endian
    // needs (16/8)*16 = 32 bytes of pixel data but we only have 0
    Assert.Throws<InvalidDataException>(() => MegaPaintReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_Parses() {
    // 16x8 image: bytesPerRow=2, pixelData=16 bytes
    var data = new byte[8 + 16];
    data[0] = 0x00; data[1] = 0x10; // width=16
    data[2] = 0x00; data[3] = 0x08; // height=8
    data[8] = 0xFF;

    var result = MegaPaintReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(16));
    Assert.That(result.Height, Is.EqualTo(8));
    Assert.That(result.PixelData.Length, Is.EqualTo(16));
    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[8 + 16];
    data[0] = 0x00; data[1] = 0x10; // width=16
    data[2] = 0x00; data[3] = 0x08; // height=8
    data[8] = 0xAB;

    using var ms = new MemoryStream(data);
    var result = MegaPaintReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(16));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesPixelData() {
    var data = new byte[8 + 16];
    data[0] = 0x00; data[1] = 0x10;
    data[2] = 0x00; data[3] = 0x08;
    data[8] = 0x42;

    var result = MegaPaintReader.FromBytes(data);
    data[8] = 0x00;

    Assert.That(result.PixelData[0], Is.EqualTo(0x42));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_AllZeros_ProducesWhiteImage() {
    var file = new MegaPaintFile {
      Width = 16,
      Height = 8,
      PixelData = new byte[16]
    };

    var raw = MegaPaintFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(16));
    Assert.That(raw.Height, Is.EqualTo(8));
    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Rgb24));
    Assert.That(raw.PixelData[0], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_AllOnes_ProducesBlackImage() {
    var pixels = new byte[16];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = 0xFF;

    var file = new MegaPaintFile {
      Width = 16,
      Height = 8,
      PixelData = pixels
    };

    var raw = MegaPaintFile.ToRawImage(file);

    Assert.That(raw.PixelData[0], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    Assert.Throws<NotSupportedException>(() => MegaPaintFile.FromRawImage(null!));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_DimensionsAndDataPreserved() {
    var file = new MegaPaintFile {
      Width = 32,
      Height = 16,
      PixelData = new byte[64]
    };
    file.PixelData[0] = 0xAA;
    file.PixelData[63] = 0x55;

    var bytes = MegaPaintWriter.ToBytes(file);
    var restored = MegaPaintReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(32));
    Assert.That(restored.Height, Is.EqualTo(16));
    Assert.That(restored.PixelData[0], Is.EqualTo(0xAA));
    Assert.That(restored.PixelData[63], Is.EqualTo(0x55));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var file = new MegaPaintFile {
      Width = 640,
      Height = 400,
      PixelData = new byte[32000]
    };
    for (var i = 0; i < file.PixelData.Length; ++i)
      file.PixelData[i] = (byte)(i * 7 % 256);

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bld");
    try {
      var bytes = MegaPaintWriter.ToBytes(file);
      File.WriteAllBytes(tempPath, bytes);

      var restored = MegaPaintReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(640));
      Assert.That(restored.Height, Is.EqualTo(400));
      Assert.That(restored.PixelData, Is.EqualTo(file.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Unit")]
  public void WriterToBytes_HeaderSize() {
    var file = new MegaPaintFile {
      Width = 16,
      Height = 8,
      PixelData = new byte[16]
    };

    var bytes = MegaPaintWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(8 + 16));
    // Check width in big-endian
    Assert.That(bytes[0], Is.EqualTo(0x00));
    Assert.That(bytes[1], Is.EqualTo(0x10));
    // Check height in big-endian
    Assert.That(bytes[2], Is.EqualTo(0x00));
    Assert.That(bytes[3], Is.EqualTo(0x08));
  }

  [Test]
  [Category("Unit")]
  public void DataType_DefaultPixelData_IsEmpty() {
    var file = new MegaPaintFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DataType_HeaderSize_Is8() {
    Assert.That(MegaPaintFile.HeaderSize, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void DataType_MinFileSize_Is8() {
    Assert.That(MegaPaintFile.MinFileSize, Is.EqualTo(8));
  }
}
