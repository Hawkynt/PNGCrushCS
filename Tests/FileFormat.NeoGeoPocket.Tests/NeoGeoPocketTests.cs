using System;
using System.IO;
using NUnit.Framework;
using FileFormat.NeoGeoPocket;
using FileFormat.Core;

namespace FileFormat.NeoGeoPocket.Tests;

[TestFixture]
public class NeoGeoPocketReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NeoGeoPocketReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => NeoGeoPocketReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NeoGeoPocketReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => NeoGeoPocketReader.FromBytes(new byte[1]));

  [Test]
  public void FromBytes_NotMultipleOfTileSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => NeoGeoPocketReader.FromBytes(new byte[16 + 1]));

  [Test]
  public void FromBytes_SingleTile_ParsesDimensions() {
    var data = new byte[16];
    var result = NeoGeoPocketReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(128));
    Assert.That(result.Height, Is.EqualTo(8));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NeoGeoPocketReader.FromStream(null!));
}

[TestFixture]
public class NeoGeoPocketWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NeoGeoPocketWriter.ToBytes(null!));

  [Test]
  public void ToBytes_SingleTile_CorrectSize() {
    var file = new NeoGeoPocketFile {
      Width = 128,
      Height = 8,
      PixelData = new byte[128 * 8],
    };
    var bytes = NeoGeoPocketWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(16 * 16));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_SingleTile_DataPreserved() {
    var original = new byte[16];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 17);
    var file = NeoGeoPocketReader.FromBytes(original);
    var written = NeoGeoPocketWriter.ToBytes(file);
    var file2 = NeoGeoPocketReader.FromBytes(written);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var data = new byte[16];
    var file = NeoGeoPocketReader.FromBytes(data);
    var raw = NeoGeoPocketFile.ToRawImage(file);
    var file2 = NeoGeoPocketFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void PrimaryExtension_IsCorrect()
    => Assert.That(_GetPrimaryExtension<NeoGeoPocketFile>(), Is.EqualTo(".ngp"));

  [Test]
  public void FileExtensions_ContainsPrimary() {
    var exts = _GetFileExtensions<NeoGeoPocketFile>();
    Assert.That(exts, Does.Contain(".ngp"));
  }

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NeoGeoPocketFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NeoGeoPocketFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 128, Height = 8, Format = PixelFormat.Rgb24, PixelData = new byte[128 * 8 * 3] };
    Assert.Throws<ArgumentException>(() => NeoGeoPocketFile.FromRawImage(raw));
  }

  [Test]
  public void Constants_AreCorrect() {
    Assert.That(NeoGeoPocketFile.BytesPerTile, Is.EqualTo(16));
    Assert.That(NeoGeoPocketFile.TileSize, Is.EqualTo(8));
    Assert.That(NeoGeoPocketFile.TilesPerRow, Is.EqualTo(16));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
