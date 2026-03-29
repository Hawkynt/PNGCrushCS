using System;
using System.IO;
using NUnit.Framework;
using FileFormat.Thomson;
using FileFormat.Core;

namespace FileFormat.Thomson.Tests;

[TestFixture]
public class ThomsonReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ThomsonReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => ThomsonReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ThomsonReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ThomsonReader.FromBytes(new byte[1]));

  [Test]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = new byte[ThomsonFile.FileSize];
    var result = ThomsonReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(ThomsonFile.ImageWidth));
    Assert.That(result.Height, Is.EqualTo(ThomsonFile.ImageHeight));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ThomsonReader.FromStream(null!));

  [Test]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = new byte[ThomsonFile.FileSize];
    using var ms = new MemoryStream(data);
    var result = ThomsonReader.FromStream(ms);
    Assert.That(result.Width, Is.EqualTo(ThomsonFile.ImageWidth));
  }
}

[TestFixture]
public class ThomsonWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ThomsonWriter.ToBytes(null!));

  [Test]
  public void ToBytes_OutputSize_IsCorrect() {
    var file = new ThomsonFile { PixelData = new byte[ThomsonFile.ImageWidth * ThomsonFile.ImageHeight] };
    var bytes = ThomsonWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(ThomsonFile.FileSize));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_DataPreserved() {
    var data = new byte[ThomsonFile.FileSize];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 1);
    var file = ThomsonReader.FromBytes(data);
    var written = ThomsonWriter.ToBytes(file);
    var file2 = ThomsonReader.FromBytes(written);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var data = new byte[ThomsonFile.FileSize];
    var file = ThomsonReader.FromBytes(data);
    var raw = ThomsonFile.ToRawImage(file);
    var file2 = ThomsonFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_AllZeros() {
    var data = new byte[ThomsonFile.FileSize];
    var file = ThomsonReader.FromBytes(data);
    var written = ThomsonWriter.ToBytes(file);
    var file2 = ThomsonReader.FromBytes(written);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void PrimaryExtension_IsCorrect()
    => Assert.That(_GetPrimaryExtension<ThomsonFile>(), Is.EqualTo(".map"));

  [Test]
  public void FileExtensions_ContainsPrimary() {
    var exts = _GetFileExtensions<ThomsonFile>();
    Assert.That(exts, Does.Contain(".map"));
  }

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ThomsonFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ThomsonFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 320, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[320 * 200 * 3] };
    Assert.Throws<ArgumentException>(() => ThomsonFile.FromRawImage(raw));
  }

  [Test]
  public void Constants_AreCorrect() {
    Assert.That(ThomsonFile.FileSize, Is.EqualTo(8000));
    Assert.That(ThomsonFile.ImageWidth, Is.EqualTo(320));
    Assert.That(ThomsonFile.ImageHeight, Is.EqualTo(200));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
