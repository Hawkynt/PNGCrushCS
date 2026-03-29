using System;
using System.IO;
using FileFormat.WonderSwanTile;
using FileFormat.Core;

namespace FileFormat.WonderSwanTile.Tests;

[TestFixture]
public class WonderSwanTileReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => WonderSwanTileReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => WonderSwanTileReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => WonderSwanTileReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => WonderSwanTileReader.FromBytes(new byte[1]));

  [Test]
  public void FromBytes_NotMultipleOfTileSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => WonderSwanTileReader.FromBytes(new byte[16 + 1]));

  [Test]
  public void FromBytes_SingleTile_ParsesDimensions() {
    var data = new byte[16];
    var result = WonderSwanTileReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(128));
    Assert.That(result.Height, Is.EqualTo(8));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => WonderSwanTileReader.FromStream(null!));
}

[TestFixture]
public class WonderSwanTileWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => WonderSwanTileWriter.ToBytes(null!));

  [Test]
  public void ToBytes_SingleTile_CorrectSize() {
    var file = new WonderSwanTileFile {
      Width = 128,
      Height = 8,
      PixelData = new byte[128 * 8],
    };
    var bytes = WonderSwanTileWriter.ToBytes(file);
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
    var file = WonderSwanTileReader.FromBytes(original);
    var written = WonderSwanTileWriter.ToBytes(file);
    var file2 = WonderSwanTileReader.FromBytes(written);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var data = new byte[16];
    var file = WonderSwanTileReader.FromBytes(data);
    var raw = WonderSwanTileFile.ToRawImage(file);
    var file2 = WonderSwanTileFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void PrimaryExtension_IsWst()
    => Assert.That(_GetPrimaryExtension<WonderSwanTileFile>(), Is.EqualTo(".wst"));

  [Test]
  public void FileExtensions_ContainsPrimary() {
    var exts = _GetFileExtensions<WonderSwanTileFile>();
    Assert.That(exts, Does.Contain(".wst"));
    Assert.That(exts, Does.Contain(".ws"));
  }

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => WonderSwanTileFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => WonderSwanTileFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 128, Height = 8, Format = PixelFormat.Rgb24, PixelData = new byte[128 * 8 * 3] };
    Assert.Throws<ArgumentException>(() => WonderSwanTileFile.FromRawImage(raw));
  }

  [Test]
  public void Constants_AreCorrect() {
    Assert.That(WonderSwanTileFile.BytesPerTile, Is.EqualTo(16));
    Assert.That(WonderSwanTileFile.TileSize, Is.EqualTo(8));
    Assert.That(WonderSwanTileFile.TilesPerRow, Is.EqualTo(16));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
