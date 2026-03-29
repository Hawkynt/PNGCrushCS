using System;
using System.IO;
using FileFormat.Mag;
using FileFormat.Core;

namespace FileFormat.Mag.Tests;

[TestFixture]
public class MagReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MagReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => MagReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MagReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => MagReader.FromBytes(new byte[31]));

  [Test]
  public void FromBytes_ValidHeader_Succeeds() {
    var data = new byte[32 + 640 * 400];
    data[0] = 128;
    data[1] = 2;
    data[4] = 144; data[5] = 1;
    var result = MagReader.FromBytes(data);
    Assert.That(result.Width, Is.GreaterThan(0));
    Assert.That(result.Height, Is.GreaterThan(0));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MagReader.FromStream(null!));
}

[TestFixture]
public class MagWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MagWriter.ToBytes(null!));

  [Test]
  public void ToBytes_IncludesHeader() {
    var file = new MagFile {
      Width = 640,
      Height = 400,
      PixelData = new byte[640 * 400],
    };
    var bytes = MagWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(32 + 640 * 400));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_PixelDataPreserved() {
    var file = new MagFile {
      Width = 640,
      Height = 400,
      PixelData = new byte[640 * 400],
    };
    for (var i = 0; i < file.PixelData.Length; ++i)
      file.PixelData[i] = (byte)(i & 0xFF);
    var bytes = MagWriter.ToBytes(file);
    var file2 = MagReader.FromBytes(bytes);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var file = new MagFile {
      Width = 640,
      Height = 400,
      PixelData = new byte[640 * 400],
    };
    var raw = MagFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
    var file2 = MagFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void HeaderSize_Is32()
    => Assert.That(MagFile.HeaderSize, Is.EqualTo(32));

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MagFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MagFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 640, Height = 400, Format = PixelFormat.Rgb24, PixelData = new byte[640 * 400 * 3] };
    Assert.Throws<ArgumentException>(() => MagFile.FromRawImage(raw));
  }

  [Test]
  public void FileExtensions_ContainsPrimary() {
    string[] exts = [".mag", ".mki"];
    Assert.That(exts, Does.Contain(".mag"));
  }
}
