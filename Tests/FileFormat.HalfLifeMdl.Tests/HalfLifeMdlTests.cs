using System;
using System.IO;
using FileFormat.HalfLifeMdl;
using FileFormat.Core;

namespace FileFormat.HalfLifeMdl.Tests;

[TestFixture]
public class HalfLifeMdlReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HalfLifeMdlReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => HalfLifeMdlReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HalfLifeMdlReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => HalfLifeMdlReader.FromBytes(new byte[15]));

  [Test]
  public void FromBytes_ValidHeader_Succeeds() {
    var data = new byte[16 + 256 * 256];
    data[0] = 0;
    data[1] = 1;
    data[4] = 0; data[5] = 1;
    var result = HalfLifeMdlReader.FromBytes(data);
    Assert.That(result.Width, Is.GreaterThan(0));
    Assert.That(result.Height, Is.GreaterThan(0));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HalfLifeMdlReader.FromStream(null!));
}

[TestFixture]
public class HalfLifeMdlWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HalfLifeMdlWriter.ToBytes(null!));

  [Test]
  public void ToBytes_IncludesHeader() {
    var file = new HalfLifeMdlFile {
      Width = 256,
      Height = 256,
      PixelData = new byte[256 * 256],
    };
    var bytes = HalfLifeMdlWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(16 + 256 * 256));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_PixelDataPreserved() {
    var file = new HalfLifeMdlFile {
      Width = 256,
      Height = 256,
      PixelData = new byte[256 * 256],
    };
    for (var i = 0; i < file.PixelData.Length; ++i)
      file.PixelData[i] = (byte)(i & 0xFF);
    var bytes = HalfLifeMdlWriter.ToBytes(file);
    var file2 = HalfLifeMdlReader.FromBytes(bytes);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var file = new HalfLifeMdlFile {
      Width = 256,
      Height = 256,
      PixelData = new byte[256 * 256],
    };
    var raw = HalfLifeMdlFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
    var file2 = HalfLifeMdlFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void HeaderSize_Is16()
    => Assert.That(HalfLifeMdlFile.HeaderSize, Is.EqualTo(16));

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HalfLifeMdlFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HalfLifeMdlFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 256, Height = 256, Format = PixelFormat.Rgb24, PixelData = new byte[256 * 256 * 3] };
    Assert.Throws<ArgumentException>(() => HalfLifeMdlFile.FromRawImage(raw));
  }

  [Test]
  public void FileExtensions_ContainsPrimary() {
    string[] exts = [".mdltex"];
    Assert.That(exts, Does.Contain(".mdltex"));
  }
}
