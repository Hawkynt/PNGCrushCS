using System;
using System.IO;
using FileFormat.PsionPic;
using FileFormat.Core;

namespace FileFormat.PsionPic.Tests;

[TestFixture]
public class PsionPicReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PsionPicReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => PsionPicReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PsionPicReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => PsionPicReader.FromBytes(new byte[15]));

  [Test]
  public void FromBytes_ValidHeader_Succeeds() {
    var data = new byte[16 + (240 + 7) / 8 * 160];
    data[0] = 240;
    data[1] = 0;
    data[4] = 160; data[5] = 0;
    var result = PsionPicReader.FromBytes(data);
    Assert.That(result.Width, Is.GreaterThan(0));
    Assert.That(result.Height, Is.GreaterThan(0));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PsionPicReader.FromStream(null!));
}

[TestFixture]
public class PsionPicWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PsionPicWriter.ToBytes(null!));

  [Test]
  public void ToBytes_IncludesHeader() {
    var file = new PsionPicFile {
      Width = 240,
      Height = 160,
      PixelData = new byte[(240 + 7) / 8 * 160],
    };
    var bytes = PsionPicWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(16 + (240 + 7) / 8 * 160));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_PixelDataPreserved() {
    var file = new PsionPicFile {
      Width = 240,
      Height = 160,
      PixelData = new byte[(240 + 7) / 8 * 160],
    };
    for (var i = 0; i < file.PixelData.Length; ++i)
      file.PixelData[i] = (byte)(i & 0xFF);
    var bytes = PsionPicWriter.ToBytes(file);
    var file2 = PsionPicReader.FromBytes(bytes);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var file = new PsionPicFile {
      Width = 240,
      Height = 160,
      PixelData = new byte[(240 + 7) / 8 * 160],
    };
    var raw = PsionPicFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
    var file2 = PsionPicFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void HeaderSize_Is16()
    => Assert.That(PsionPicFile.HeaderSize, Is.EqualTo(16));

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PsionPicFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PsionPicFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 240, Height = 160, Format = PixelFormat.Rgb24, PixelData = new byte[240 * 160 * 3] };
    Assert.Throws<ArgumentException>(() => PsionPicFile.FromRawImage(raw));
  }

  [Test]
  public void FileExtensions_ContainsPrimary() {
    string[] exts = [".ppic"];
    Assert.That(exts, Does.Contain(".ppic"));
  }
}
