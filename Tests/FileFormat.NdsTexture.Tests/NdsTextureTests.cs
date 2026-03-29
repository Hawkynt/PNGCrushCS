using System;
using System.IO;
using FileFormat.NdsTexture;
using FileFormat.Core;

namespace FileFormat.NdsTexture.Tests;

[TestFixture]
public class NdsTextureReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NdsTextureReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => NdsTextureReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NdsTextureReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => NdsTextureReader.FromBytes(new byte[1]));

  [Test]
  public void FromBytes_NotMultipleOfTileSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => NdsTextureReader.FromBytes(new byte[32 + 1]));

  [Test]
  public void FromBytes_SingleTile_ParsesDimensions() {
    var data = new byte[32];
    var result = NdsTextureReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(128));
    Assert.That(result.Height, Is.EqualTo(8));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NdsTextureReader.FromStream(null!));
}

[TestFixture]
public class NdsTextureWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NdsTextureWriter.ToBytes(null!));

  [Test]
  public void ToBytes_SingleTile_CorrectSize() {
    var file = new NdsTextureFile {
      Width = 128,
      Height = 8,
      PixelData = new byte[128 * 8],
    };
    var bytes = NdsTextureWriter.ToBytes(file);
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
    var file = NdsTextureReader.FromBytes(original);
    var written = NdsTextureWriter.ToBytes(file);
    var file2 = NdsTextureReader.FromBytes(written);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var data = new byte[32];
    var file = NdsTextureReader.FromBytes(data);
    var raw = NdsTextureFile.ToRawImage(file);
    var file2 = NdsTextureFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void PrimaryExtension_IsNbfs()
    => Assert.That(_GetPrimaryExtension<NdsTextureFile>(), Is.EqualTo(".nbfs"));

  [Test]
  public void FileExtensions_ContainsPrimary() {
    var exts = _GetFileExtensions<NdsTextureFile>();
    Assert.That(exts, Does.Contain(".nbfs"));
    Assert.That(exts, Does.Contain(".nds"));
  }

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NdsTextureFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NdsTextureFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 128, Height = 8, Format = PixelFormat.Rgb24, PixelData = new byte[128 * 8 * 3] };
    Assert.Throws<ArgumentException>(() => NdsTextureFile.FromRawImage(raw));
  }

  [Test]
  public void Constants_AreCorrect() {
    Assert.That(NdsTextureFile.BytesPerTile, Is.EqualTo(32));
    Assert.That(NdsTextureFile.TileSize, Is.EqualTo(8));
    Assert.That(NdsTextureFile.TilesPerRow, Is.EqualTo(16));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
