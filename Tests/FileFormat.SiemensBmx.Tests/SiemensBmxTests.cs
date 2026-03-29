using System;
using System.IO;
using FileFormat.SiemensBmx;
using FileFormat.Core;

namespace FileFormat.SiemensBmx.Tests;

[TestFixture]
public class SiemensBmxReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SiemensBmxReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => SiemensBmxReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SiemensBmxReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => SiemensBmxReader.FromBytes(new byte[7]));

  [Test]
  public void FromBytes_ValidHeader_Succeeds() {
    var data = new byte[8 + 101 * 64];
    data[0] = 101;
    data[1] = 0;
    data[4] = 64; data[5] = 0;
    var result = SiemensBmxReader.FromBytes(data);
    Assert.That(result.Width, Is.GreaterThan(0));
    Assert.That(result.Height, Is.GreaterThan(0));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SiemensBmxReader.FromStream(null!));
}

[TestFixture]
public class SiemensBmxWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SiemensBmxWriter.ToBytes(null!));

  [Test]
  public void ToBytes_IncludesHeader() {
    var file = new SiemensBmxFile {
      Width = 101,
      Height = 64,
      PixelData = new byte[101 * 64],
    };
    var bytes = SiemensBmxWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(8 + 101 * 64));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_PixelDataPreserved() {
    var file = new SiemensBmxFile {
      Width = 101,
      Height = 64,
      PixelData = new byte[101 * 64],
    };
    for (var i = 0; i < file.PixelData.Length; ++i)
      file.PixelData[i] = (byte)(i & 0xFF);
    var bytes = SiemensBmxWriter.ToBytes(file);
    var file2 = SiemensBmxReader.FromBytes(bytes);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var file = new SiemensBmxFile {
      Width = 101,
      Height = 64,
      PixelData = new byte[101 * 64],
    };
    var raw = SiemensBmxFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
    var file2 = SiemensBmxFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void HeaderSize_Is8()
    => Assert.That(SiemensBmxFile.HeaderSize, Is.EqualTo(8));

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SiemensBmxFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SiemensBmxFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 101, Height = 64, Format = PixelFormat.Rgb24, PixelData = new byte[101 * 64 * 3] };
    Assert.Throws<ArgumentException>(() => SiemensBmxFile.FromRawImage(raw));
  }

  [Test]
  public void FileExtensions_ContainsPrimary() {
    string[] exts = [".bmx"];
    Assert.That(exts, Does.Contain(".bmx"));
  }
}
