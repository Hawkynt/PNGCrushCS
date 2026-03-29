using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.AmigaIcon;

namespace FileFormat.AmigaIcon.Tests;

[TestFixture]
public sealed class AmigaIconReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AmigaIconReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AmigaIconReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".info"));
    Assert.Throws<FileNotFoundException>(() => AmigaIconReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AmigaIconReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => AmigaIconReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[78 + 4];
    data[0] = 0x00;
    data[1] = 0x00;
    Assert.Throws<InvalidDataException>(() => AmigaIconReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroImageDataPointer_ThrowsInvalidDataException() {
    var data = _BuildMinimalHeader(16, 16, 2, iconType: 3, imageDataPointer: 0);
    Assert.Throws<InvalidDataException>(() => AmigaIconReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidDepth_ThrowsInvalidDataException() {
    var data = _BuildMinimalHeader(16, 16, 0);
    Assert.Throws<InvalidDataException>(() => AmigaIconReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesDimensions() {
    var width = 32;
    var height = 16;
    var depth = 2;
    var planarSize = AmigaIconFile.PlanarDataSize(width, height, depth);
    var data = _BuildMinimalHeader(width, height, depth, planarDataSize: planarSize);

    // Set a known byte in the planar data
    data[AmigaIconHeader.StructSize] = 0xAA;

    var result = AmigaIconReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Height, Is.EqualTo(height));
    Assert.That(result.Depth, Is.EqualTo(depth));
    Assert.That(result.PlanarData.Length, Is.EqualTo(planarSize));
    Assert.That(result.PlanarData[0], Is.EqualTo(0xAA));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesIconType() {
    var width = 16;
    var height = 16;
    var depth = 2;
    var planarSize = AmigaIconFile.PlanarDataSize(width, height, depth);
    var data = _BuildMinimalHeader(width, height, depth, iconType: (int)AmigaIconType.Drawer, planarDataSize: planarSize);

    var result = AmigaIconReader.FromBytes(data);

    Assert.That(result.IconType, Is.EqualTo((int)AmigaIconType.Drawer));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid_ParsesCorrectly() {
    var width = 16;
    var height = 8;
    var depth = 1;
    var planarSize = AmigaIconFile.PlanarDataSize(width, height, depth);
    var data = _BuildMinimalHeader(width, height, depth, planarDataSize: planarSize);
    data[AmigaIconHeader.StructSize] = 0xFF;

    using var ms = new MemoryStream(data);
    var result = AmigaIconReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Height, Is.EqualTo(height));
    Assert.That(result.Depth, Is.EqualTo(depth));
    Assert.That(result.PlanarData[0], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PreservesRawHeader() {
    var width = 16;
    var height = 8;
    var depth = 1;
    var planarSize = AmigaIconFile.PlanarDataSize(width, height, depth);
    var data = _BuildMinimalHeader(width, height, depth, planarDataSize: planarSize);

    var result = AmigaIconReader.FromBytes(data);

    Assert.That(result.RawHeader, Is.Not.Null);
    Assert.That(result.RawHeader!.Length, Is.EqualTo(AmigaIconHeader.StructSize));
    Assert.That(result.RawHeader[0], Is.EqualTo(0xE3));
    Assert.That(result.RawHeader[1], Is.EqualTo(0x10));
  }

  private static byte[] _BuildMinimalHeader(int width, int height, int depth, int iconType = 3, ushort imageDataPointer = 1, int planarDataSize = -1) {
    if (planarDataSize < 0)
      planarDataSize = AmigaIconFile.PlanarDataSize(width, height, depth);

    var data = new byte[AmigaIconHeader.StructSize + planarDataSize];
    var span = data.AsSpan();

    BinaryPrimitives.WriteUInt16BigEndian(span[0..], 0xE310);
    BinaryPrimitives.WriteUInt16BigEndian(span[2..], 1);
    BinaryPrimitives.WriteInt16BigEndian(span[10..], (short)width);
    BinaryPrimitives.WriteInt16BigEndian(span[12..], (short)height);
    BinaryPrimitives.WriteInt16BigEndian(span[14..], (short)depth);
    BinaryPrimitives.WriteUInt16BigEndian(span[16..], imageDataPointer);
    span[54] = (byte)iconType;

    return data;
  }
}
