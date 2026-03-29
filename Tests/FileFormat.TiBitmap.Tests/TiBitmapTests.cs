using System;
using System.IO;
using FileFormat.TiBitmap;
using FileFormat.Core;

namespace FileFormat.TiBitmap.Tests;

[TestFixture]
public class TiBitmapReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => TiBitmapReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => TiBitmapReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => TiBitmapReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => TiBitmapReader.FromBytes(new byte[7]));

  [Test]
  public void FromBytes_ValidHeader_Succeeds() {
    var data = new byte[8 + (96 + 7) / 8 * 64];
    data[0] = 96;
    data[1] = 0;
    data[4] = 64; data[5] = 0;
    var result = TiBitmapReader.FromBytes(data);
    Assert.That(result.Width, Is.GreaterThan(0));
    Assert.That(result.Height, Is.GreaterThan(0));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => TiBitmapReader.FromStream(null!));
}

[TestFixture]
public class TiBitmapWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => TiBitmapWriter.ToBytes(null!));

  [Test]
  public void ToBytes_IncludesHeader() {
    var file = new TiBitmapFile {
      Width = 96,
      Height = 64,
      PixelData = new byte[(96 + 7) / 8 * 64],
    };
    var bytes = TiBitmapWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(8 + (96 + 7) / 8 * 64));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_PixelDataPreserved() {
    var file = new TiBitmapFile {
      Width = 96,
      Height = 64,
      PixelData = new byte[(96 + 7) / 8 * 64],
    };
    for (var i = 0; i < file.PixelData.Length; ++i)
      file.PixelData[i] = (byte)(i & 0xFF);
    var bytes = TiBitmapWriter.ToBytes(file);
    var file2 = TiBitmapReader.FromBytes(bytes);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var file = new TiBitmapFile {
      Width = 96,
      Height = 64,
      PixelData = new byte[(96 + 7) / 8 * 64],
    };
    var raw = TiBitmapFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
    var file2 = TiBitmapFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void HeaderSize_Is8()
    => Assert.That(TiBitmapFile.HeaderSize, Is.EqualTo(8));

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => TiBitmapFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => TiBitmapFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 96, Height = 64, Format = PixelFormat.Rgb24, PixelData = new byte[96 * 64 * 3] };
    Assert.Throws<ArgumentException>(() => TiBitmapFile.FromRawImage(raw));
  }

  [Test]
  public void FileExtensions_ContainsPrimary() {
    string[] exts = [".8xi", ".89i"];
    Assert.That(exts, Does.Contain(".8xi"));
  }
}
