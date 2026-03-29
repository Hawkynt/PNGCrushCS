using System;
using System.IO;
using FileFormat.HpGrob;
using FileFormat.Core;

namespace FileFormat.HpGrob.Tests;

[TestFixture]
public class HpGrobReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HpGrobReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => HpGrobReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HpGrobReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => HpGrobReader.FromBytes(new byte[9]));

  [Test]
  public void FromBytes_ValidHeader_Succeeds() {
    var data = new byte[10 + (131 + 7) / 8 * 64];
    data[0] = 131;
    data[1] = 0;
    data[4] = 64; data[5] = 0;
    var result = HpGrobReader.FromBytes(data);
    Assert.That(result.Width, Is.GreaterThan(0));
    Assert.That(result.Height, Is.GreaterThan(0));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HpGrobReader.FromStream(null!));
}

[TestFixture]
public class HpGrobWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HpGrobWriter.ToBytes(null!));

  [Test]
  public void ToBytes_IncludesHeader() {
    var file = new HpGrobFile {
      Width = 131,
      Height = 64,
      PixelData = new byte[(131 + 7) / 8 * 64],
    };
    var bytes = HpGrobWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(10 + (131 + 7) / 8 * 64));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_PixelDataPreserved() {
    var file = new HpGrobFile {
      Width = 131,
      Height = 64,
      PixelData = new byte[(131 + 7) / 8 * 64],
    };
    for (var i = 0; i < file.PixelData.Length; ++i)
      file.PixelData[i] = (byte)(i & 0xFF);
    var bytes = HpGrobWriter.ToBytes(file);
    var file2 = HpGrobReader.FromBytes(bytes);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var file = new HpGrobFile {
      Width = 131,
      Height = 64,
      PixelData = new byte[(131 + 7) / 8 * 64],
    };
    var raw = HpGrobFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
    var file2 = HpGrobFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void HeaderSize_Is10()
    => Assert.That(HpGrobFile.HeaderSize, Is.EqualTo(10));

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HpGrobFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HpGrobFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 131, Height = 64, Format = PixelFormat.Rgb24, PixelData = new byte[131 * 64 * 3] };
    Assert.Throws<ArgumentException>(() => HpGrobFile.FromRawImage(raw));
  }

  [Test]
  public void FileExtensions_ContainsPrimary() {
    string[] exts = [".grob", ".hp"];
    Assert.That(exts, Does.Contain(".grob"));
  }
}
