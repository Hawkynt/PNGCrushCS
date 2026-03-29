using System;
using System.IO;
using FileFormat.VirtualBoyTile;
using FileFormat.Core;

namespace FileFormat.VirtualBoyTile.Tests;

[TestFixture]
public class VirtualBoyTileReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => VirtualBoyTileReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => VirtualBoyTileReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => VirtualBoyTileReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => VirtualBoyTileReader.FromBytes(new byte[1]));

  [Test]
  public void FromBytes_NotMultipleOfTileSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => VirtualBoyTileReader.FromBytes(new byte[16 + 1]));

  [Test]
  public void FromBytes_SingleTile_ParsesDimensions() {
    var data = new byte[16];
    var result = VirtualBoyTileReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(128));
    Assert.That(result.Height, Is.EqualTo(8));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => VirtualBoyTileReader.FromStream(null!));
}

[TestFixture]
public class VirtualBoyTileWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => VirtualBoyTileWriter.ToBytes(null!));

  [Test]
  public void ToBytes_SingleTile_CorrectSize() {
    var file = new VirtualBoyTileFile {
      Width = 128,
      Height = 8,
      PixelData = new byte[128 * 8],
    };
    var bytes = VirtualBoyTileWriter.ToBytes(file);
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
    var file = VirtualBoyTileReader.FromBytes(original);
    var written = VirtualBoyTileWriter.ToBytes(file);
    var file2 = VirtualBoyTileReader.FromBytes(written);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var data = new byte[16];
    var file = VirtualBoyTileReader.FromBytes(data);
    var raw = VirtualBoyTileFile.ToRawImage(file);
    var file2 = VirtualBoyTileFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void PrimaryExtension_IsVbt()
    => Assert.That(_GetPrimaryExtension<VirtualBoyTileFile>(), Is.EqualTo(".vbt"));

  [Test]
  public void FileExtensions_ContainsPrimary() {
    var exts = _GetFileExtensions<VirtualBoyTileFile>();
    Assert.That(exts, Does.Contain(".vbt"));
    Assert.That(exts, Does.Contain(".vb"));
  }

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => VirtualBoyTileFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => VirtualBoyTileFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 128, Height = 8, Format = PixelFormat.Rgb24, PixelData = new byte[128 * 8 * 3] };
    Assert.Throws<ArgumentException>(() => VirtualBoyTileFile.FromRawImage(raw));
  }

  [Test]
  public void Constants_AreCorrect() {
    Assert.That(VirtualBoyTileFile.BytesPerTile, Is.EqualTo(16));
    Assert.That(VirtualBoyTileFile.TileSize, Is.EqualTo(8));
    Assert.That(VirtualBoyTileFile.TilesPerRow, Is.EqualTo(16));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
