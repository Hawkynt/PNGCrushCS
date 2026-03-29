using System;
using System.IO;
using NUnit.Framework;
using FileFormat.SharpX68k;
using FileFormat.Core;

namespace FileFormat.SharpX68k.Tests;

[TestFixture]
public class SharpX68kReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SharpX68kReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => SharpX68kReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SharpX68kReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => SharpX68kReader.FromBytes(new byte[1]));

  [Test]
  public void FromBytes_ValidData_ParsesDimensions() {
    var header = new byte[SharpX68kFile.HeaderSize + 512 * 512 * 6];
    header[0] = 512 & 0xFF;
    header[1] = (512 >> 8) & 0xFF;
    header[2] = 512 & 0xFF;
    header[3] = (512 >> 8) & 0xFF;
    var result = SharpX68kReader.FromBytes(header);
    Assert.That(result.Width, Is.EqualTo(512));
    Assert.That(result.Height, Is.EqualTo(512));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SharpX68kReader.FromStream(null!));
}

[TestFixture]
public class SharpX68kWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SharpX68kWriter.ToBytes(null!));

  [Test]
  public void ToBytes_HeaderContainsDimensions() {
    var file = new SharpX68kFile { Width = 512, Height = 512, PixelData = new byte[512 * 512 * 3] };
    var bytes = SharpX68kWriter.ToBytes(file);
    Assert.That(bytes[0] | (bytes[1] << 8), Is.EqualTo(512));
    Assert.That(bytes[2] | (bytes[3] << 8), Is.EqualTo(512));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_DimensionsPreserved() {
    var file = new SharpX68kFile { Width = 512, Height = 512, PixelData = new byte[512 * 512 * 3] };
    var bytes = SharpX68kWriter.ToBytes(file);
    var file2 = SharpX68kReader.FromBytes(bytes);
    Assert.That(file2.Width, Is.EqualTo(file.Width));
    Assert.That(file2.Height, Is.EqualTo(file.Height));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var file = new SharpX68kFile { Width = 512, Height = 512, PixelData = new byte[512 * 512 * 3] };
    var raw = SharpX68kFile.ToRawImage(file);
    var file2 = SharpX68kFile.FromRawImage(raw);
    Assert.That(file2.Width, Is.EqualTo(file.Width));
    Assert.That(file2.Height, Is.EqualTo(file.Height));
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void PrimaryExtension_IsCorrect()
    => Assert.That(_GetPrimaryExtension<SharpX68kFile>(), Is.EqualTo(".x68"));

  [Test]
  public void FileExtensions_ContainsPrimary() {
    var exts = _GetFileExtensions<SharpX68kFile>();
    Assert.That(exts, Does.Contain(".x68"));
  }

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SharpX68kFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SharpX68kFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 512, Height = 512, Format = PixelFormat.Indexed1, PixelData = new byte[512 * 512 * 1] };
    Assert.Throws<ArgumentException>(() => SharpX68kFile.FromRawImage(raw));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
