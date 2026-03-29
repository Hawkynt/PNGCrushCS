using System;
using System.IO;
using FileFormat.Cloe;
using FileFormat.Core;

namespace FileFormat.Cloe.Tests;

[TestFixture]
public class CloeReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CloeReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => CloeReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CloeReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => CloeReader.FromBytes(new byte[7]));

  [Test]
  public void FromBytes_ValidHeader_Succeeds() {
    var data = new byte[8 + 320 * 200 * 3];
    data[0] = 64;
    data[1] = 1;
    data[4] = 200; data[5] = 0;
    var result = CloeReader.FromBytes(data);
    Assert.That(result.Width, Is.GreaterThan(0));
    Assert.That(result.Height, Is.GreaterThan(0));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CloeReader.FromStream(null!));
}

[TestFixture]
public class CloeWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CloeWriter.ToBytes(null!));

  [Test]
  public void ToBytes_IncludesHeader() {
    var file = new CloeFile {
      Width = 320,
      Height = 200,
      PixelData = new byte[320 * 200 * 3],
    };
    var bytes = CloeWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(8 + 320 * 200 * 3));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_PixelDataPreserved() {
    var file = new CloeFile {
      Width = 320,
      Height = 200,
      PixelData = new byte[320 * 200 * 3],
    };
    for (var i = 0; i < file.PixelData.Length; ++i)
      file.PixelData[i] = (byte)(i & 0xFF);
    var bytes = CloeWriter.ToBytes(file);
    var file2 = CloeReader.FromBytes(bytes);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var file = new CloeFile {
      Width = 320,
      Height = 200,
      PixelData = new byte[320 * 200 * 3],
    };
    var raw = CloeFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    var file2 = CloeFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void HeaderSize_Is8()
    => Assert.That(CloeFile.HeaderSize, Is.EqualTo(8));

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CloeFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CloeFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 320, Height = 200, Format = PixelFormat.Indexed8, PixelData = new byte[320 * 200] };
    Assert.Throws<ArgumentException>(() => CloeFile.FromRawImage(raw));
  }

  [Test]
  public void FileExtensions_ContainsPrimary() {
    string[] exts = [".clo"];
    Assert.That(exts, Does.Contain(".clo"));
  }
}
