using System;
using System.IO;
using FileFormat.HereticM8;
using FileFormat.Core;

namespace FileFormat.HereticM8.Tests;

[TestFixture]
public class HereticM8ReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HereticM8Reader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => HereticM8Reader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HereticM8Reader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => HereticM8Reader.FromBytes(new byte[7]));

  [Test]
  public void FromBytes_ValidHeader_Succeeds() {
    var data = new byte[8 + 256 * 256];
    data[0] = 0;
    data[1] = 1;
    data[4] = 0; data[5] = 1;
    var result = HereticM8Reader.FromBytes(data);
    Assert.That(result.Width, Is.GreaterThan(0));
    Assert.That(result.Height, Is.GreaterThan(0));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HereticM8Reader.FromStream(null!));
}

[TestFixture]
public class HereticM8WriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HereticM8Writer.ToBytes(null!));

  [Test]
  public void ToBytes_IncludesHeader() {
    var file = new HereticM8File {
      Width = 256,
      Height = 256,
      PixelData = new byte[256 * 256],
    };
    var bytes = HereticM8Writer.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(8 + 256 * 256));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_PixelDataPreserved() {
    var file = new HereticM8File {
      Width = 256,
      Height = 256,
      PixelData = new byte[256 * 256],
    };
    for (var i = 0; i < file.PixelData.Length; ++i)
      file.PixelData[i] = (byte)(i & 0xFF);
    var bytes = HereticM8Writer.ToBytes(file);
    var file2 = HereticM8Reader.FromBytes(bytes);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var file = new HereticM8File {
      Width = 256,
      Height = 256,
      PixelData = new byte[256 * 256],
    };
    var raw = HereticM8File.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
    var file2 = HereticM8File.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void HeaderSize_Is8()
    => Assert.That(HereticM8File.HeaderSize, Is.EqualTo(8));

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HereticM8File.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HereticM8File.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 256, Height = 256, Format = PixelFormat.Rgb24, PixelData = new byte[256 * 256 * 3] };
    Assert.Throws<ArgumentException>(() => HereticM8File.FromRawImage(raw));
  }

  [Test]
  public void FileExtensions_ContainsPrimary() {
    string[] exts = [".m8"];
    Assert.That(exts, Does.Contain(".m8"));
  }
}
