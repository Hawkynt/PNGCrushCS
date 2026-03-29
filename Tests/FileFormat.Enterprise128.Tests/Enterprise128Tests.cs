using System;
using System.IO;
using NUnit.Framework;
using FileFormat.Enterprise128;
using FileFormat.Core;

namespace FileFormat.Enterprise128.Tests;

[TestFixture]
public class Enterprise128ReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Enterprise128Reader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => Enterprise128Reader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Enterprise128Reader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => Enterprise128Reader.FromBytes(new byte[1]));

  [Test]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = new byte[Enterprise128File.FileSize];
    var result = Enterprise128Reader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(Enterprise128File.ImageWidth));
    Assert.That(result.Height, Is.EqualTo(Enterprise128File.ImageHeight));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Enterprise128Reader.FromStream(null!));

  [Test]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = new byte[Enterprise128File.FileSize];
    using var ms = new MemoryStream(data);
    var result = Enterprise128Reader.FromStream(ms);
    Assert.That(result.Width, Is.EqualTo(Enterprise128File.ImageWidth));
  }
}

[TestFixture]
public class Enterprise128WriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Enterprise128Writer.ToBytes(null!));

  [Test]
  public void ToBytes_OutputSize_IsCorrect() {
    var file = new Enterprise128File { PixelData = new byte[Enterprise128File.ImageWidth * Enterprise128File.ImageHeight] };
    var bytes = Enterprise128Writer.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(Enterprise128File.FileSize));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_DataPreserved() {
    var data = new byte[Enterprise128File.FileSize];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 1);
    var file = Enterprise128Reader.FromBytes(data);
    var written = Enterprise128Writer.ToBytes(file);
    var file2 = Enterprise128Reader.FromBytes(written);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var data = new byte[Enterprise128File.FileSize];
    var file = Enterprise128Reader.FromBytes(data);
    var raw = Enterprise128File.ToRawImage(file);
    var file2 = Enterprise128File.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_AllZeros() {
    var data = new byte[Enterprise128File.FileSize];
    var file = Enterprise128Reader.FromBytes(data);
    var written = Enterprise128Writer.ToBytes(file);
    var file2 = Enterprise128Reader.FromBytes(written);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void PrimaryExtension_IsCorrect()
    => Assert.That(_GetPrimaryExtension<Enterprise128File>(), Is.EqualTo(".ep"));

  [Test]
  public void FileExtensions_ContainsPrimary() {
    var exts = _GetFileExtensions<Enterprise128File>();
    Assert.That(exts, Does.Contain(".ep"));
  }

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Enterprise128File.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Enterprise128File.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 512, Height = 256, Format = PixelFormat.Rgb24, PixelData = new byte[512 * 256 * 3] };
    Assert.Throws<ArgumentException>(() => Enterprise128File.FromRawImage(raw));
  }

  [Test]
  public void Constants_AreCorrect() {
    Assert.That(Enterprise128File.FileSize, Is.EqualTo(16384));
    Assert.That(Enterprise128File.ImageWidth, Is.EqualTo(512));
    Assert.That(Enterprise128File.ImageHeight, Is.EqualTo(256));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
