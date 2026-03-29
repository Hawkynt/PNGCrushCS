using System;
using System.IO;
using FileFormat.KofaxKfx;
using FileFormat.Core;

namespace FileFormat.KofaxKfx.Tests;

[TestFixture]
public class KofaxKfxReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => KofaxKfxReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => KofaxKfxReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => KofaxKfxReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => KofaxKfxReader.FromBytes(new byte[15]));

  [Test]
  public void FromBytes_ValidHeader_Succeeds() {
    var data = new byte[16 + (1728 + 7) / 8 * 2200];
    data[0] = 192;
    data[1] = 6;
    data[4] = 152; data[5] = 8;
    var result = KofaxKfxReader.FromBytes(data);
    Assert.That(result.Width, Is.GreaterThan(0));
    Assert.That(result.Height, Is.GreaterThan(0));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => KofaxKfxReader.FromStream(null!));
}

[TestFixture]
public class KofaxKfxWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => KofaxKfxWriter.ToBytes(null!));

  [Test]
  public void ToBytes_IncludesHeader() {
    var file = new KofaxKfxFile {
      Width = 1728,
      Height = 2200,
      PixelData = new byte[(1728 + 7) / 8 * 2200],
    };
    var bytes = KofaxKfxWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(16 + (1728 + 7) / 8 * 2200));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_PixelDataPreserved() {
    var file = new KofaxKfxFile {
      Width = 1728,
      Height = 2200,
      PixelData = new byte[(1728 + 7) / 8 * 2200],
    };
    for (var i = 0; i < file.PixelData.Length; ++i)
      file.PixelData[i] = (byte)(i & 0xFF);
    var bytes = KofaxKfxWriter.ToBytes(file);
    var file2 = KofaxKfxReader.FromBytes(bytes);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var file = new KofaxKfxFile {
      Width = 1728,
      Height = 2200,
      PixelData = new byte[(1728 + 7) / 8 * 2200],
    };
    var raw = KofaxKfxFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
    var file2 = KofaxKfxFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void HeaderSize_Is16()
    => Assert.That(KofaxKfxFile.HeaderSize, Is.EqualTo(16));

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => KofaxKfxFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => KofaxKfxFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 1728, Height = 2200, Format = PixelFormat.Rgb24, PixelData = new byte[1728 * 2200 * 3] };
    Assert.Throws<ArgumentException>(() => KofaxKfxFile.FromRawImage(raw));
  }

  [Test]
  public void FileExtensions_ContainsPrimary() {
    string[] exts = [".kfx"];
    Assert.That(exts, Does.Contain(".kfx"));
  }
}
