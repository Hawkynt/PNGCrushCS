using System;
using System.IO;
using FileFormat.Ipl;
using FileFormat.Core;

namespace FileFormat.Ipl.Tests;

[TestFixture]
public class IplReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => IplReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => IplReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => IplReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => IplReader.FromBytes(new byte[15]));

  [Test]
  public void FromBytes_ValidHeader_Succeeds() {
    var data = new byte[16 + 320 * 240 * 3];
    data[0] = 64;
    data[1] = 1;
    data[4] = 240; data[5] = 0;
    var result = IplReader.FromBytes(data);
    Assert.That(result.Width, Is.GreaterThan(0));
    Assert.That(result.Height, Is.GreaterThan(0));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => IplReader.FromStream(null!));
}

[TestFixture]
public class IplWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => IplWriter.ToBytes(null!));

  [Test]
  public void ToBytes_IncludesHeader() {
    var file = new IplFile {
      Width = 320,
      Height = 240,
      PixelData = new byte[320 * 240 * 3],
    };
    var bytes = IplWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(16 + 320 * 240 * 3));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_PixelDataPreserved() {
    var file = new IplFile {
      Width = 320,
      Height = 240,
      PixelData = new byte[320 * 240 * 3],
    };
    for (var i = 0; i < file.PixelData.Length; ++i)
      file.PixelData[i] = (byte)(i & 0xFF);
    var bytes = IplWriter.ToBytes(file);
    var file2 = IplReader.FromBytes(bytes);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var file = new IplFile {
      Width = 320,
      Height = 240,
      PixelData = new byte[320 * 240 * 3],
    };
    var raw = IplFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    var file2 = IplFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void HeaderSize_Is16()
    => Assert.That(IplFile.HeaderSize, Is.EqualTo(16));

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => IplFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => IplFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 320, Height = 240, Format = PixelFormat.Indexed8, PixelData = new byte[320 * 240] };
    Assert.Throws<ArgumentException>(() => IplFile.FromRawImage(raw));
  }

  [Test]
  public void FileExtensions_ContainsPrimary() {
    string[] exts = [".ipl"];
    Assert.That(exts, Does.Contain(".ipl"));
  }
}
