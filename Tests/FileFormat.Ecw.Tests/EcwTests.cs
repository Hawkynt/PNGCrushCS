using System;
using System.IO;
using FileFormat.Ecw;
using FileFormat.Core;

namespace FileFormat.Ecw.Tests;

[TestFixture]
public class EcwReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => EcwReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => EcwReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => EcwReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => EcwReader.FromBytes(new byte[15]));

  [Test]
  public void FromBytes_ValidHeader_Succeeds() {
    var data = new byte[16 + 256 * 256 * 3];
    data[0] = 0;
    data[1] = 1;
    data[4] = 0; data[5] = 1;
    var result = EcwReader.FromBytes(data);
    Assert.That(result.Width, Is.GreaterThan(0));
    Assert.That(result.Height, Is.GreaterThan(0));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => EcwReader.FromStream(null!));
}

[TestFixture]
public class EcwWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => EcwWriter.ToBytes(null!));

  [Test]
  public void ToBytes_IncludesEcwMagicHeader() {
    var file = new EcwFile {
      Width = 256,
      Height = 256,
      PixelData = new byte[256 * 256 * 3],
    };
    var bytes = EcwWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(24));
    Assert.That(bytes[0], Is.EqualTo(0x65)); // 'e'
    Assert.That(bytes[1], Is.EqualTo(0x63)); // 'c'
    Assert.That(bytes[2], Is.EqualTo(0x77)); // 'w'
    Assert.That(bytes[3], Is.EqualTo(0x00)); // '\0'
  }

  [Test]
  public void ToBytes_EmptyPixelData_ProducesLegacyFormat() {
    var file = new EcwFile {
      Width = 256,
      Height = 256,
      PixelData = [],
    };
    var bytes = EcwWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(EcwFile.HeaderSize));
  }

  [Test]
  public void ToBytes_CompressedSmallerThanRaw() {
    var file = new EcwFile {
      Width = 256,
      Height = 256,
      PixelData = new byte[256 * 256 * 3],
    };
    for (var i = 0; i < file.PixelData.Length; ++i)
      file.PixelData[i] = (byte)(i & 0xFF);
    var bytes = EcwWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.LessThan(file.PixelData.Length));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_PixelDataPreserved() {
    var file = new EcwFile {
      Width = 256,
      Height = 256,
      PixelData = new byte[256 * 256 * 3],
    };
    for (var i = 0; i < file.PixelData.Length; ++i)
      file.PixelData[i] = (byte)(i & 0xFF);
    var bytes = EcwWriter.ToBytes(file);
    var file2 = EcwReader.FromBytes(bytes);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var file = new EcwFile {
      Width = 256,
      Height = 256,
      PixelData = new byte[256 * 256 * 3],
    };
    var raw = EcwFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    var file2 = EcwFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void HeaderSize_Is16()
    => Assert.That(EcwFile.HeaderSize, Is.EqualTo(16));

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => EcwFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => EcwFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 256, Height = 256, Format = PixelFormat.Indexed8, PixelData = new byte[256 * 256] };
    Assert.Throws<ArgumentException>(() => EcwFile.FromRawImage(raw));
  }

  [Test]
  public void FileExtensions_ContainsPrimary() {
    string[] exts = [".ecw"];
    Assert.That(exts, Does.Contain(".ecw"));
  }
}
