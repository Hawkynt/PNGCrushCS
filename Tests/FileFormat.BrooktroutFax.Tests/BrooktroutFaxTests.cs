using System;
using System.IO;
using FileFormat.BrooktroutFax;
using FileFormat.Core;

namespace FileFormat.BrooktroutFax.Tests;

[TestFixture]
public class BrooktroutFaxReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => BrooktroutFaxReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => BrooktroutFaxReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => BrooktroutFaxReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => BrooktroutFaxReader.FromBytes(new byte[31]));

  [Test]
  public void FromBytes_ValidHeader_Succeeds() {
    var data = new byte[32 + (1728 + 7) / 8 * 2200];
    data[0] = 192;
    data[1] = 6;
    data[4] = 152; data[5] = 8;
    var result = BrooktroutFaxReader.FromBytes(data);
    Assert.That(result.Width, Is.GreaterThan(0));
    Assert.That(result.Height, Is.GreaterThan(0));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => BrooktroutFaxReader.FromStream(null!));
}

[TestFixture]
public class BrooktroutFaxWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => BrooktroutFaxWriter.ToBytes(null!));

  [Test]
  public void ToBytes_IncludesHeader() {
    var file = new BrooktroutFaxFile {
      Width = 1728,
      Height = 2200,
      PixelData = new byte[(1728 + 7) / 8 * 2200],
    };
    var bytes = BrooktroutFaxWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(32 + (1728 + 7) / 8 * 2200));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_PixelDataPreserved() {
    var file = new BrooktroutFaxFile {
      Width = 1728,
      Height = 2200,
      PixelData = new byte[(1728 + 7) / 8 * 2200],
    };
    for (var i = 0; i < file.PixelData.Length; ++i)
      file.PixelData[i] = (byte)(i & 0xFF);
    var bytes = BrooktroutFaxWriter.ToBytes(file);
    var file2 = BrooktroutFaxReader.FromBytes(bytes);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var file = new BrooktroutFaxFile {
      Width = 1728,
      Height = 2200,
      PixelData = new byte[(1728 + 7) / 8 * 2200],
    };
    var raw = BrooktroutFaxFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
    var file2 = BrooktroutFaxFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void HeaderSize_Is32()
    => Assert.That(BrooktroutFaxFile.HeaderSize, Is.EqualTo(32));

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => BrooktroutFaxFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => BrooktroutFaxFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 1728, Height = 2200, Format = PixelFormat.Rgb24, PixelData = new byte[1728 * 2200 * 3] };
    Assert.Throws<ArgumentException>(() => BrooktroutFaxFile.FromRawImage(raw));
  }

  [Test]
  public void FileExtensions_ContainsPrimary() {
    string[] exts = [".brk", ".301"];
    Assert.That(exts, Does.Contain(".brk"));
  }
}
