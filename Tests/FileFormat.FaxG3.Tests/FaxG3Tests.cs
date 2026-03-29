using System;
using System.IO;
using FileFormat.FaxG3;
using FileFormat.Core;

namespace FileFormat.FaxG3.Tests;

[TestFixture]
public class FaxG3ReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => FaxG3Reader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => FaxG3Reader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => FaxG3Reader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => FaxG3Reader.FromBytes(new byte[5]));

  [Test]
  public void FromBytes_ValidHeader_Succeeds() {
    var data = new byte[6 + (1728 + 7) / 8 * 2200];
    data[0] = 192;
    data[1] = 6;
    data[2] = 152; data[3] = 8;
    var result = FaxG3Reader.FromBytes(data);
    Assert.That(result.Width, Is.GreaterThan(0));
    Assert.That(result.Height, Is.GreaterThan(0));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => FaxG3Reader.FromStream(null!));
}

[TestFixture]
public class FaxG3WriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => FaxG3Writer.ToBytes(null!));

  [Test]
  public void ToBytes_IncludesHeader() {
    var file = new FaxG3File {
      Width = 1728,
      Height = 2200,
      PixelData = new byte[(1728 + 7) / 8 * 2200],
    };
    var bytes = FaxG3Writer.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(6 + (1728 + 7) / 8 * 2200));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_PixelDataPreserved() {
    var file = new FaxG3File {
      Width = 1728,
      Height = 2200,
      PixelData = new byte[(1728 + 7) / 8 * 2200],
    };
    for (var i = 0; i < file.PixelData.Length; ++i)
      file.PixelData[i] = (byte)(i & 0xFF);
    var bytes = FaxG3Writer.ToBytes(file);
    var file2 = FaxG3Reader.FromBytes(bytes);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var file = new FaxG3File {
      Width = 1728,
      Height = 2200,
      PixelData = new byte[(1728 + 7) / 8 * 2200],
    };
    var raw = FaxG3File.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
    var file2 = FaxG3File.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void HeaderSize_Is6()
    => Assert.That(FaxG3File.HeaderSize, Is.EqualTo(6));

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => FaxG3File.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => FaxG3File.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 1728, Height = 2200, Format = PixelFormat.Rgb24, PixelData = new byte[1728 * 2200 * 3] };
    Assert.Throws<ArgumentException>(() => FaxG3File.FromRawImage(raw));
  }

  [Test]
  public void FileExtensions_ContainsPrimary() {
    string[] exts = [".g3"];
    Assert.That(exts, Does.Contain(".g3"));
  }
}
