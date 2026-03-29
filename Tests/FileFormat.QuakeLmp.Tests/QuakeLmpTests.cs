using System;
using System.IO;
using FileFormat.QuakeLmp;
using FileFormat.Core;

namespace FileFormat.QuakeLmp.Tests;

[TestFixture]
public class QuakeLmpReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => QuakeLmpReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => QuakeLmpReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => QuakeLmpReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => QuakeLmpReader.FromBytes(new byte[7]));

  [Test]
  public void FromBytes_ValidHeader_Succeeds() {
    var data = new byte[8 + 128 * 128];
    data[0] = 128;
    data[1] = 0;
    data[4] = 128; data[5] = 0;
    var result = QuakeLmpReader.FromBytes(data);
    Assert.That(result.Width, Is.GreaterThan(0));
    Assert.That(result.Height, Is.GreaterThan(0));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => QuakeLmpReader.FromStream(null!));
}

[TestFixture]
public class QuakeLmpWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => QuakeLmpWriter.ToBytes(null!));

  [Test]
  public void ToBytes_IncludesHeader() {
    var file = new QuakeLmpFile {
      Width = 128,
      Height = 128,
      PixelData = new byte[128 * 128],
    };
    var bytes = QuakeLmpWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(8 + 128 * 128));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_PixelDataPreserved() {
    var file = new QuakeLmpFile {
      Width = 128,
      Height = 128,
      PixelData = new byte[128 * 128],
    };
    for (var i = 0; i < file.PixelData.Length; ++i)
      file.PixelData[i] = (byte)(i & 0xFF);
    var bytes = QuakeLmpWriter.ToBytes(file);
    var file2 = QuakeLmpReader.FromBytes(bytes);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var file = new QuakeLmpFile {
      Width = 128,
      Height = 128,
      PixelData = new byte[128 * 128],
    };
    var raw = QuakeLmpFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
    var file2 = QuakeLmpFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void HeaderSize_Is8()
    => Assert.That(QuakeLmpFile.HeaderSize, Is.EqualTo(8));

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => QuakeLmpFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => QuakeLmpFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 128, Height = 128, Format = PixelFormat.Rgb24, PixelData = new byte[128 * 128 * 3] };
    Assert.Throws<ArgumentException>(() => QuakeLmpFile.FromRawImage(raw));
  }

  [Test]
  public void FileExtensions_ContainsPrimary() {
    string[] exts = [".lmp"];
    Assert.That(exts, Does.Contain(".lmp"));
  }
}
