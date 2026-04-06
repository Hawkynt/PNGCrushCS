using System;
using System.IO;
using FileFormat.NeoGeoSprite;
using FileFormat.Core;

namespace FileFormat.NeoGeoSprite.Tests;

[TestFixture]
public class NeoGeoSpriteReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NeoGeoSpriteReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => NeoGeoSpriteReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NeoGeoSpriteReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => NeoGeoSpriteReader.FromBytes(new byte[1]));

  [Test]
  public void FromBytes_NotMultipleOfTileSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => NeoGeoSpriteReader.FromBytes(new byte[32 + 1]));

  [Test]
  public void FromBytes_SingleTile_ParsesDimensions() {
    var data = new byte[32];
    var result = NeoGeoSpriteReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(128));
    Assert.That(result.Height, Is.EqualTo(8));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NeoGeoSpriteReader.FromStream(null!));
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_SingleTile_DataPreserved() {
    var original = new byte[32];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 17);
    var file = NeoGeoSpriteReader.FromBytes(original);
    var written = NeoGeoSpriteWriter.ToBytes(file);
    var file2 = NeoGeoSpriteReader.FromBytes(written);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var data = new byte[32];
    var file = NeoGeoSpriteReader.FromBytes(data);
    var raw = NeoGeoSpriteFile.ToRawImage(file);
    var file2 = NeoGeoSpriteFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

