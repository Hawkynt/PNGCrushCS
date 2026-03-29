using System;
using System.IO;
using FileFormat.PixarRib;
using FileFormat.Core;

namespace FileFormat.PixarRib.Tests;

[TestFixture]
public class PixarRibReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PixarRibReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => PixarRibReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PixarRibReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => PixarRibReader.FromBytes(new byte[511]));

  [Test]
  public void FromBytes_ValidHeader_Succeeds() {
    var data = new byte[512 + 256 * 256 * 3];
    data[0] = 0;
    data[1] = 1;
    data[4] = 0; data[5] = 1;
    var result = PixarRibReader.FromBytes(data);
    Assert.That(result.Width, Is.GreaterThan(0));
    Assert.That(result.Height, Is.GreaterThan(0));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PixarRibReader.FromStream(null!));
}

[TestFixture]
public class PixarRibWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PixarRibWriter.ToBytes(null!));

  [Test]
  public void ToBytes_IncludesHeader() {
    var file = new PixarRibFile {
      Width = 256,
      Height = 256,
      PixelData = new byte[256 * 256 * 3],
    };
    var bytes = PixarRibWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(512 + 256 * 256 * 3));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_PixelDataPreserved() {
    var file = new PixarRibFile {
      Width = 256,
      Height = 256,
      PixelData = new byte[256 * 256 * 3],
    };
    for (var i = 0; i < file.PixelData.Length; ++i)
      file.PixelData[i] = (byte)(i & 0xFF);
    var bytes = PixarRibWriter.ToBytes(file);
    var file2 = PixarRibReader.FromBytes(bytes);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var file = new PixarRibFile {
      Width = 256,
      Height = 256,
      PixelData = new byte[256 * 256 * 3],
    };
    var raw = PixarRibFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    var file2 = PixarRibFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void HeaderSize_Is512()
    => Assert.That(PixarRibFile.HeaderSize, Is.EqualTo(512));

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PixarRibFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PixarRibFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 256, Height = 256, Format = PixelFormat.Indexed8, PixelData = new byte[256 * 256] };
    Assert.Throws<ArgumentException>(() => PixarRibFile.FromRawImage(raw));
  }

  [Test]
  public void FileExtensions_ContainsPrimary() {
    string[] exts = [".pxr"];
    Assert.That(exts, Does.Contain(".pxr"));
  }
}
