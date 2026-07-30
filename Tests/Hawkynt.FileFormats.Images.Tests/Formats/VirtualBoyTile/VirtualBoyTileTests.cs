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

