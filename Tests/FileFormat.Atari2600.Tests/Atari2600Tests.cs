using System;
using System.IO;
using NUnit.Framework;
using FileFormat.Atari2600;
using FileFormat.Core;

namespace FileFormat.Atari2600.Tests;

[TestFixture]
public class Atari2600ReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Atari2600Reader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => Atari2600Reader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Atari2600Reader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => Atari2600Reader.FromBytes(new byte[1]));

  [Test]
  public void FromBytes_NotMultipleOfTileSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => Atari2600Reader.FromBytes(new byte[8 + 1]));

  [Test]
  public void FromBytes_SingleTile_ParsesDimensions() {
    var data = new byte[8];
    var result = Atari2600Reader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(128));
    Assert.That(result.Height, Is.EqualTo(8));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Atari2600Reader.FromStream(null!));
}

[TestFixture]
public class Atari2600WriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Atari2600Writer.ToBytes(null!));

  [Test]
  public void ToBytes_SingleTile_CorrectSize() {
    var file = new Atari2600File {
      Width = 128,
      Height = 8,
      PixelData = new byte[128 * 8],
    };
    var bytes = Atari2600Writer.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(16 * 8));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_SingleTile_DataPreserved() {
    var original = new byte[8];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 17);
    var file = Atari2600Reader.FromBytes(original);
    var written = Atari2600Writer.ToBytes(file);
    var file2 = Atari2600Reader.FromBytes(written);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var data = new byte[8];
    var file = Atari2600Reader.FromBytes(data);
    var raw = Atari2600File.ToRawImage(file);
    var file2 = Atari2600File.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void PrimaryExtension_IsCorrect()
    => Assert.That(_GetPrimaryExtension<Atari2600File>(), Is.EqualTo(".a26"));

  [Test]
  public void FileExtensions_ContainsPrimary() {
    var exts = _GetFileExtensions<Atari2600File>();
    Assert.That(exts, Does.Contain(".a26"));
  }

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Atari2600File.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Atari2600File.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 128, Height = 8, Format = PixelFormat.Rgb24, PixelData = new byte[128 * 8 * 3] };
    Assert.Throws<ArgumentException>(() => Atari2600File.FromRawImage(raw));
  }

  [Test]
  public void Constants_AreCorrect() {
    Assert.That(Atari2600File.BytesPerTile, Is.EqualTo(8));
    Assert.That(Atari2600File.TileSize, Is.EqualTo(8));
    Assert.That(Atari2600File.TilesPerRow, Is.EqualTo(16));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
