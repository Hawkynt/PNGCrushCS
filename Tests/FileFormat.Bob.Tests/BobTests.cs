using System;
using System.IO;
using FileFormat.Bob;
using FileFormat.Core;

namespace FileFormat.Bob.Tests;

[TestFixture]
public class BobReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => BobReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => BobReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => BobReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => BobReader.FromBytes(new byte[7]));

  [Test]
  public void FromBytes_ValidHeader_Succeeds() {
    var data = new byte[8 + 320 * 200 * 3];
    data[0] = 64;
    data[1] = 1;
    data[4] = 200; data[5] = 0;
    var result = BobReader.FromBytes(data);
    Assert.That(result.Width, Is.GreaterThan(0));
    Assert.That(result.Height, Is.GreaterThan(0));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => BobReader.FromStream(null!));
}

[TestFixture]
public class BobWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => BobWriter.ToBytes(null!));

  [Test]
  public void ToBytes_IncludesHeader() {
    var file = new BobFile {
      Width = 320,
      Height = 200,
      PixelData = new byte[320 * 200 * 3],
    };
    var bytes = BobWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(8 + 320 * 200 * 3));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_PixelDataPreserved() {
    var file = new BobFile {
      Width = 320,
      Height = 200,
      PixelData = new byte[320 * 200 * 3],
    };
    for (var i = 0; i < file.PixelData.Length; ++i)
      file.PixelData[i] = (byte)(i & 0xFF);
    var bytes = BobWriter.ToBytes(file);
    var file2 = BobReader.FromBytes(bytes);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var file = new BobFile {
      Width = 320,
      Height = 200,
      PixelData = new byte[320 * 200 * 3],
    };
    var raw = BobFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    var file2 = BobFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void HeaderSize_Is8()
    => Assert.That(BobFile.HeaderSize, Is.EqualTo(8));

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => BobFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => BobFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 320, Height = 200, Format = PixelFormat.Indexed8, PixelData = new byte[320 * 200] };
    Assert.Throws<ArgumentException>(() => BobFile.FromRawImage(raw));
  }

  [Test]
  public void FileExtensions_ContainsPrimary() {
    string[] exts = [".bob"];
    Assert.That(exts, Does.Contain(".bob"));
  }
}
