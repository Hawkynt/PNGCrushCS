using System;
using System.IO;
using FileFormat.GbaTile;
using FileFormat.Core;

namespace FileFormat.GbaTile.Tests;

[TestFixture]
public class GbaTileReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => GbaTileReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => GbaTileReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => GbaTileReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => GbaTileReader.FromBytes(new byte[1]));

  [Test]
  public void FromBytes_NotMultipleOfTileSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => GbaTileReader.FromBytes(new byte[32 + 1]));

  [Test]
  public void FromBytes_SingleTile_ParsesDimensions() {
    var data = new byte[32];
    var result = GbaTileReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(128));
    Assert.That(result.Height, Is.EqualTo(8));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => GbaTileReader.FromStream(null!));
}

[TestFixture]
public class GbaTileWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => GbaTileWriter.ToBytes(null!));

  [Test]
  public void ToBytes_SingleTile_CorrectSize() {
    var file = new GbaTileFile {
      Width = 128,
      Height = 8,
      PixelData = new byte[128 * 8],
    };
    var bytes = GbaTileWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(16 * 32));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_SingleTile_DataPreserved() {
    var original = new byte[32];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 17);
    var file = GbaTileReader.FromBytes(original);
    var written = GbaTileWriter.ToBytes(file);
    var file2 = GbaTileReader.FromBytes(written);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var data = new byte[32];
    var file = GbaTileReader.FromBytes(data);
    var raw = GbaTileFile.ToRawImage(file);
    var file2 = GbaTileFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void PrimaryExtension_Is4bpp()
    => Assert.That(_GetPrimaryExtension<GbaTileFile>(), Is.EqualTo(".4bpp"));

  [Test]
  public void FileExtensions_ContainsPrimary() {
    var exts = _GetFileExtensions<GbaTileFile>();
    Assert.That(exts, Does.Contain(".4bpp"));
    Assert.That(exts, Does.Contain(".gba"));
  }

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => GbaTileFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => GbaTileFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 128, Height = 8, Format = PixelFormat.Rgb24, PixelData = new byte[128 * 8 * 3] };
    Assert.Throws<ArgumentException>(() => GbaTileFile.FromRawImage(raw));
  }

  [Test]
  public void Constants_AreCorrect() {
    Assert.That(GbaTileFile.BytesPerTile, Is.EqualTo(32));
    Assert.That(GbaTileFile.TileSize, Is.EqualTo(8));
    Assert.That(GbaTileFile.TilesPerRow, Is.EqualTo(16));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
