using System;
using System.IO;
using NUnit.Framework;
using FileFormat.CommodorePet;
using FileFormat.Core;

namespace FileFormat.CommodorePet.Tests;

[TestFixture]
public class CommodorePetReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CommodorePetReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => CommodorePetReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CommodorePetReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => CommodorePetReader.FromBytes(new byte[1]));

  [Test]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = new byte[CommodorePetFile.FileSize];
    var result = CommodorePetReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(CommodorePetFile.ImageWidth));
    Assert.That(result.Height, Is.EqualTo(CommodorePetFile.ImageHeight));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CommodorePetReader.FromStream(null!));

  [Test]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = new byte[CommodorePetFile.FileSize];
    using var ms = new MemoryStream(data);
    var result = CommodorePetReader.FromStream(ms);
    Assert.That(result.Width, Is.EqualTo(CommodorePetFile.ImageWidth));
  }
}

[TestFixture]
public class CommodorePetWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CommodorePetWriter.ToBytes(null!));

  [Test]
  public void ToBytes_OutputSize_IsCorrect() {
    var file = new CommodorePetFile { PixelData = new byte[CommodorePetFile.ImageWidth * CommodorePetFile.ImageHeight] };
    var bytes = CommodorePetWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(CommodorePetFile.FileSize));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_DataPreserved() {
    var data = new byte[CommodorePetFile.FileSize];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 0xFF);
    var file = CommodorePetReader.FromBytes(data);
    var written = CommodorePetWriter.ToBytes(file);
    var file2 = CommodorePetReader.FromBytes(written);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var data = new byte[CommodorePetFile.FileSize];
    var file = CommodorePetReader.FromBytes(data);
    var raw = CommodorePetFile.ToRawImage(file);
    var file2 = CommodorePetFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_AllZeros() {
    var data = new byte[CommodorePetFile.FileSize];
    var file = CommodorePetReader.FromBytes(data);
    var written = CommodorePetWriter.ToBytes(file);
    var file2 = CommodorePetReader.FromBytes(written);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void PrimaryExtension_IsCorrect()
    => Assert.That(_GetPrimaryExtension<CommodorePetFile>(), Is.EqualTo(".pet"));

  [Test]
  public void FileExtensions_ContainsPrimary() {
    var exts = _GetFileExtensions<CommodorePetFile>();
    Assert.That(exts, Does.Contain(".pet"));
  }

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CommodorePetFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CommodorePetFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 40, Height = 25, Format = PixelFormat.Rgba32, PixelData = new byte[40 * 25 * 4] };
    Assert.Throws<ArgumentException>(() => CommodorePetFile.FromRawImage(raw));
  }

  [Test]
  public void Constants_AreCorrect() {
    Assert.That(CommodorePetFile.FileSize, Is.EqualTo(1000));
    Assert.That(CommodorePetFile.ImageWidth, Is.EqualTo(40));
    Assert.That(CommodorePetFile.ImageHeight, Is.EqualTo(25));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
