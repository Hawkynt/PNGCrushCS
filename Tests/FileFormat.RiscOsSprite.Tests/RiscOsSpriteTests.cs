using System;
using System.IO;
using NUnit.Framework;
using FileFormat.RiscOsSprite;
using FileFormat.Core;

namespace FileFormat.RiscOsSprite.Tests;

[TestFixture]
public class RiscOsSpriteReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => RiscOsSpriteReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => RiscOsSpriteReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => RiscOsSpriteReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => RiscOsSpriteReader.FromBytes(new byte[1]));

  [Test]
  public void FromBytes_ValidData_ParsesDimensions() {
    var header = new byte[RiscOsSpriteFile.HeaderSize + 320 * 256 * 6];
    header[0] = 320 & 0xFF;
    header[1] = (320 >> 8) & 0xFF;
    header[2] = 256 & 0xFF;
    header[3] = (256 >> 8) & 0xFF;
    var result = RiscOsSpriteReader.FromBytes(header);
    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(256));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => RiscOsSpriteReader.FromStream(null!));
}

[TestFixture]
public class RiscOsSpriteWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => RiscOsSpriteWriter.ToBytes(null!));

  [Test]
  public void ToBytes_HeaderContainsDimensions() {
    var file = new RiscOsSpriteFile { Width = 320, Height = 256, PixelData = new byte[320 * 256 * 3] };
    var bytes = RiscOsSpriteWriter.ToBytes(file);
    Assert.That(bytes[0] | (bytes[1] << 8), Is.EqualTo(320));
    Assert.That(bytes[2] | (bytes[3] << 8), Is.EqualTo(256));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_DimensionsPreserved() {
    var file = new RiscOsSpriteFile { Width = 320, Height = 256, PixelData = new byte[320 * 256 * 3] };
    var bytes = RiscOsSpriteWriter.ToBytes(file);
    var file2 = RiscOsSpriteReader.FromBytes(bytes);
    Assert.That(file2.Width, Is.EqualTo(file.Width));
    Assert.That(file2.Height, Is.EqualTo(file.Height));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var file = new RiscOsSpriteFile { Width = 320, Height = 256, PixelData = new byte[320 * 256 * 3] };
    var raw = RiscOsSpriteFile.ToRawImage(file);
    var file2 = RiscOsSpriteFile.FromRawImage(raw);
    Assert.That(file2.Width, Is.EqualTo(file.Width));
    Assert.That(file2.Height, Is.EqualTo(file.Height));
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void PrimaryExtension_IsCorrect()
    => Assert.That(_GetPrimaryExtension<RiscOsSpriteFile>(), Is.EqualTo(".spr"));

  [Test]
  public void FileExtensions_ContainsPrimary() {
    var exts = _GetFileExtensions<RiscOsSpriteFile>();
    Assert.That(exts, Does.Contain(".spr"));
  }

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => RiscOsSpriteFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => RiscOsSpriteFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 320, Height = 256, Format = PixelFormat.Indexed1, PixelData = new byte[320 * 256 * 1] };
    Assert.Throws<ArgumentException>(() => RiscOsSpriteFile.FromRawImage(raw));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
