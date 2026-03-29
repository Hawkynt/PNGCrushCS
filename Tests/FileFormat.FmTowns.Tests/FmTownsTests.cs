using System;
using System.IO;
using NUnit.Framework;
using FileFormat.FmTowns;
using FileFormat.Core;

namespace FileFormat.FmTowns.Tests;

[TestFixture]
public class FmTownsReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => FmTownsReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => FmTownsReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => FmTownsReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => FmTownsReader.FromBytes(new byte[1]));

  [Test]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = new byte[FmTownsFile.FileSize];
    var result = FmTownsReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(FmTownsFile.ImageWidth));
    Assert.That(result.Height, Is.EqualTo(FmTownsFile.ImageHeight));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => FmTownsReader.FromStream(null!));

  [Test]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = new byte[FmTownsFile.FileSize];
    using var ms = new MemoryStream(data);
    var result = FmTownsReader.FromStream(ms);
    Assert.That(result.Width, Is.EqualTo(FmTownsFile.ImageWidth));
  }
}

[TestFixture]
public class FmTownsWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => FmTownsWriter.ToBytes(null!));

  [Test]
  public void ToBytes_OutputSize_IsCorrect() {
    var file = new FmTownsFile { PixelData = new byte[FmTownsFile.ImageWidth * FmTownsFile.ImageHeight] };
    var bytes = FmTownsWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(FmTownsFile.FileSize));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_DataPreserved() {
    var data = new byte[FmTownsFile.FileSize];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 0xFF);
    var file = FmTownsReader.FromBytes(data);
    var written = FmTownsWriter.ToBytes(file);
    var file2 = FmTownsReader.FromBytes(written);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var data = new byte[FmTownsFile.FileSize];
    var file = FmTownsReader.FromBytes(data);
    var raw = FmTownsFile.ToRawImage(file);
    var file2 = FmTownsFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_AllZeros() {
    var data = new byte[FmTownsFile.FileSize];
    var file = FmTownsReader.FromBytes(data);
    var written = FmTownsWriter.ToBytes(file);
    var file2 = FmTownsReader.FromBytes(written);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void PrimaryExtension_IsCorrect()
    => Assert.That(_GetPrimaryExtension<FmTownsFile>(), Is.EqualTo(".fmt"));

  [Test]
  public void FileExtensions_ContainsPrimary() {
    var exts = _GetFileExtensions<FmTownsFile>();
    Assert.That(exts, Does.Contain(".fmt"));
  }

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => FmTownsFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => FmTownsFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 320, Height = 200, Format = PixelFormat.Rgba32, PixelData = new byte[320 * 200 * 4] };
    Assert.Throws<ArgumentException>(() => FmTownsFile.FromRawImage(raw));
  }

  [Test]
  public void Constants_AreCorrect() {
    Assert.That(FmTownsFile.FileSize, Is.EqualTo(64000));
    Assert.That(FmTownsFile.ImageWidth, Is.EqualTo(320));
    Assert.That(FmTownsFile.ImageHeight, Is.EqualTo(200));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
