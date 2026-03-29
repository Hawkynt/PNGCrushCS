using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.SpookySpritesFalcon;

namespace FileFormat.SpookySpritesFalcon.Tests;

[TestFixture]
public sealed class SpookySpritesFalconReaderTests {

  private static byte[] _MakeMinimalFile(ushort width, ushort height) {
    var header = new byte[SpookySpritesFalconHeader.StructSize + 1];
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(0), width);
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(2), height);
    header[SpookySpritesFalconHeader.StructSize] = 0; // end marker
    return header;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SpookySpritesFalconReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SpookySpritesFalconReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tre"));
    Assert.Throws<FileNotFoundException>(() => SpookySpritesFalconReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SpookySpritesFalconReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[2];
    Assert.Throws<InvalidDataException>(() => SpookySpritesFalconReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroWidth_ThrowsInvalidDataException() {
    var data = _MakeMinimalFile(0, 10);
    Assert.Throws<InvalidDataException>(() => SpookySpritesFalconReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroHeight_ThrowsInvalidDataException() {
    var data = _MakeMinimalFile(10, 0);
    Assert.Throws<InvalidDataException>(() => SpookySpritesFalconReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesDimensions() {
    var data = _MakeMinimalFile(8, 4);

    var result = SpookySpritesFalconReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(4));
    Assert.That(result.PixelData.Length, Is.EqualTo(8 * 4 * 2));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = _MakeMinimalFile(4, 3);

    using var ms = new MemoryStream(data);
    var result = SpookySpritesFalconReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithLiteralRle_DecodesPixels() {
    // 2x1 image: 2 literal pixels
    var data = new byte[SpookySpritesFalconHeader.StructSize + 1 + 4 + 1];
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0), 2);
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), 1);
    data[4] = 2;      // literal count = 2
    data[5] = 0xF8;   // pixel 1 hi
    data[6] = 0x00;   // pixel 1 lo
    data[7] = 0x07;   // pixel 2 hi
    data[8] = 0xE0;   // pixel 2 lo
    data[9] = 0;      // end marker

    var result = SpookySpritesFalconReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(0xF8));
    Assert.That(result.PixelData[1], Is.EqualTo(0x00));
    Assert.That(result.PixelData[2], Is.EqualTo(0x07));
    Assert.That(result.PixelData[3], Is.EqualTo(0xE0));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithRepeatRle_DecodesPixels() {
    // 3x1 image: repeat 0xF800 three times
    var data = new byte[SpookySpritesFalconHeader.StructSize + 1 + 2 + 1];
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0), 3);
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), 1);
    data[4] = unchecked((byte)(sbyte)-3); // repeat count = -3
    data[5] = 0xF8;   // pixel hi
    data[6] = 0x00;   // pixel lo
    data[7] = 0;      // end marker

    var result = SpookySpritesFalconReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(0xF8));
    Assert.That(result.PixelData[1], Is.EqualTo(0x00));
    Assert.That(result.PixelData[2], Is.EqualTo(0xF8));
    Assert.That(result.PixelData[3], Is.EqualTo(0x00));
    Assert.That(result.PixelData[4], Is.EqualTo(0xF8));
    Assert.That(result.PixelData[5], Is.EqualTo(0x00));
  }
}
