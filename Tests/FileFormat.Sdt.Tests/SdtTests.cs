using System;
using System.IO;
using FileFormat.Sdt;
using FileFormat.Core;

namespace FileFormat.Sdt.Tests;

[TestFixture]
public class SdtReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SdtReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => SdtReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SdtReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => SdtReader.FromBytes(new byte[7]));

  [Test]
  public void FromBytes_ValidHeader_Succeeds() {
    var data = new byte[8 + 128 * 128 * 3];
    data[0] = 128;
    data[1] = 0;
    data[4] = 128; data[5] = 0;
    var result = SdtReader.FromBytes(data);
    Assert.That(result.Width, Is.GreaterThan(0));
    Assert.That(result.Height, Is.GreaterThan(0));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SdtReader.FromStream(null!));
}

[TestFixture]
public class SdtWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SdtWriter.ToBytes(null!));

  [Test]
  public void ToBytes_IncludesHeader() {
    var file = new SdtFile {
      Width = 128,
      Height = 128,
      PixelData = new byte[128 * 128 * 3],
    };
    var bytes = SdtWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(8 + 128 * 128 * 3));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_PixelDataPreserved() {
    var file = new SdtFile {
      Width = 128,
      Height = 128,
      PixelData = new byte[128 * 128 * 3],
    };
    for (var i = 0; i < file.PixelData.Length; ++i)
      file.PixelData[i] = (byte)(i & 0xFF);
    var bytes = SdtWriter.ToBytes(file);
    var file2 = SdtReader.FromBytes(bytes);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var file = new SdtFile {
      Width = 128,
      Height = 128,
      PixelData = new byte[128 * 128 * 3],
    };
    var raw = SdtFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    var file2 = SdtFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void HeaderSize_Is8()
    => Assert.That(SdtFile.HeaderSize, Is.EqualTo(8));

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SdtFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SdtFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 128, Height = 128, Format = PixelFormat.Indexed8, PixelData = new byte[128 * 128] };
    Assert.Throws<ArgumentException>(() => SdtFile.FromRawImage(raw));
  }

  [Test]
  public void FileExtensions_ContainsPrimary() {
    string[] exts = [".sdt"];
    Assert.That(exts, Does.Contain(".sdt"));
  }
}
