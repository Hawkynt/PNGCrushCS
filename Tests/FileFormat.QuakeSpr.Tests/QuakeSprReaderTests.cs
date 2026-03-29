using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.QuakeSpr;

namespace FileFormat.QuakeSpr.Tests;

[TestFixture]
public sealed class QuakeSprReaderTests {

  private static byte[] _BuildValidSpr(int width, int height, byte fillByte = 0x00) {
    var pixelCount = width * height;
    var data = new byte[36 + 20 + pixelCount];
    var span = data.AsSpan();

    // Magic "IDSP"
    span[0] = 0x49;
    span[1] = 0x44;
    span[2] = 0x53;
    span[3] = 0x50;
    BinaryPrimitives.WriteInt32LittleEndian(span[4..], 1);      // version
    BinaryPrimitives.WriteInt32LittleEndian(span[8..], 0);      // type
    BitConverter.TryWriteBytes(span[12..], 32.0f);              // boundingRadius
    BinaryPrimitives.WriteInt32LittleEndian(span[16..], width); // maxWidth
    BinaryPrimitives.WriteInt32LittleEndian(span[20..], height);// maxHeight
    BinaryPrimitives.WriteInt32LittleEndian(span[24..], 1);     // numFrames
    BitConverter.TryWriteBytes(span[28..], 0.0f);               // beamLength
    BinaryPrimitives.WriteInt32LittleEndian(span[32..], 0);     // syncType

    // Frame header
    BinaryPrimitives.WriteInt32LittleEndian(span[36..], 0);      // frameType = single
    BinaryPrimitives.WriteInt32LittleEndian(span[40..], 0);      // originX
    BinaryPrimitives.WriteInt32LittleEndian(span[44..], 0);      // originY
    BinaryPrimitives.WriteInt32LittleEndian(span[48..], width);  // width
    BinaryPrimitives.WriteInt32LittleEndian(span[52..], height); // height

    // Pixel data
    for (var i = 56; i < data.Length; ++i)
      data[i] = fillByte;

    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => QuakeSprReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => QuakeSprReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".spr"));
    Assert.Throws<FileNotFoundException>(() => QuakeSprReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => QuakeSprReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[20];
    Assert.Throws<InvalidDataException>(() => QuakeSprReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[56 + 4];
    // Write wrong magic
    data[0] = 0x00;
    data[1] = 0x00;
    data[2] = 0x00;
    data[3] = 0x00;
    Assert.Throws<InvalidDataException>(() => QuakeSprReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidVersion_ThrowsInvalidDataException() {
    var data = new byte[56 + 4];
    var span = data.AsSpan();
    span[0] = 0x49;
    span[1] = 0x44;
    span[2] = 0x53;
    span[3] = 0x50;
    BinaryPrimitives.WriteInt32LittleEndian(span[4..], 2); // wrong version
    BinaryPrimitives.WriteInt32LittleEndian(span[24..], 1); // numFrames
    BinaryPrimitives.WriteInt32LittleEndian(span[48..], 2); // width
    BinaryPrimitives.WriteInt32LittleEndian(span[52..], 2); // height
    Assert.Throws<InvalidDataException>(() => QuakeSprReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidSingleFrame() {
    var data = _BuildValidSpr(4, 3, 0xAB);

    var result = QuakeSprReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.NumFrames, Is.EqualTo(1));
    Assert.That(result.SpriteType, Is.EqualTo(0));
    Assert.That(result.PixelData.Length, Is.EqualTo(12));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidSingleFrame_PreservesHeaderFields() {
    var data = _BuildValidSpr(2, 2);
    var span = data.AsSpan();
    BinaryPrimitives.WriteInt32LittleEndian(span[8..], 3);   // type = ORIENTED
    BitConverter.TryWriteBytes(span[12..], 64.5f);           // boundingRadius
    BitConverter.TryWriteBytes(span[28..], 1.5f);            // beamLength
    BinaryPrimitives.WriteInt32LittleEndian(span[32..], 1);  // syncType = random

    var result = QuakeSprReader.FromBytes(data);

    Assert.That(result.SpriteType, Is.EqualTo(3));
    Assert.That(result.BoundingRadius, Is.EqualTo(64.5f));
    Assert.That(result.BeamLength, Is.EqualTo(1.5f));
    Assert.That(result.SyncType, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = _BuildValidSpr(2, 2);
    using var ms = new MemoryStream(data);

    var result = QuakeSprReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
  }
}
