using System;
using System.IO;
using FileFormat.PcPaint;

namespace FileFormat.PcPaint.Tests;

[TestFixture]
public sealed class PcPaintReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PcPaintReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PcPaintReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pic"));
    Assert.Throws<FileNotFoundException>(() => PcPaintReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PcPaintReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => PcPaintReader.FromBytes(new byte[10]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = _Build(4, 3);
    bad[0] = 0xFF;
    bad[1] = 0xFF;
    Assert.Throws<InvalidDataException>(() => PcPaintReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroWidth_ThrowsInvalidDataException() {
    var data = _Build(4, 3);
    data[2] = 0;
    data[3] = 0;
    Assert.Throws<InvalidDataException>(() => PcPaintReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroHeight_ThrowsInvalidDataException() {
    var data = _Build(4, 3);
    data[4] = 0;
    data[5] = 0;
    Assert.Throws<InvalidDataException>(() => PcPaintReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_UnknownDepth_ThrowsInvalidDataException() {
    var data = _Build(4, 3);
    data[10] = 0x03;
    Assert.Throws<InvalidDataException>(() => PcPaintReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SeveralPlanes_ThrowsInvalidDataException() {
    var data = _Build(4, 3);
    data[10] = 0x31;
    Assert.Throws<InvalidDataException>(() => PcPaintReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_BeforeVersionTwo_ThrowsInvalidDataException() {
    var data = _Build(4, 3);
    data[11] = 0x00;
    Assert.Throws<InvalidDataException>(() => PcPaintReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_BlockOverrunsFile_ThrowsInvalidDataException() {
    var data = _Build(4, 3);
    var blockAt = PcPaintFile.HeaderSize + PcPaintFile.VgaPaletteBytes + 2;
    data[blockAt] = 0xFF;
    data[blockAt + 1] = 0xFF;
    Assert.Throws<InvalidDataException>(() => PcPaintReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_StatedRunLengthDisagrees_ThrowsInvalidDataException() {
    var data = _Build(4, 3);
    var blockAt = PcPaintFile.HeaderSize + PcPaintFile.VgaPaletteBytes + 2;
    data[blockAt + 2] = 99;
    Assert.Throws<InvalidDataException>(() => PcPaintReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesDimensions() {
    var result = PcPaintReader.FromBytes(_Build(4, 3));

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.BitsPerPixel, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesOffsets() {
    var data = _Build(4, 3);
    data[6] = 10;
    data[8] = 20;

    var result = PcPaintReader.FromBytes(data);

    Assert.That(result.XOffset, Is.EqualTo(10));
    Assert.That(result.YOffset, Is.EqualTo(20));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ReadsRowsFromTheBottomUpwards() {
    // Three rows of four, stored bottom row first: the last row written is the picture's top.
    var result = PcPaintReader.FromBytes(_Build(4, 3));

    Assert.That(result.PixelData[0], Is.EqualTo(2));
    Assert.That(result.PixelData[4], Is.EqualTo(1));
    Assert.That(result.PixelData[8], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid_ParsesCorrectly() {
    using var ms = new MemoryStream(_Build(3, 2));
    var result = PcPaintReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(3));
    Assert.That(result.Height, Is.EqualTo(2));
  }

  /// <summary>
  /// A version 2 page of the given size, eight bits a pixel with a VGA palette, whose stored rows
  /// carry their own index as every pixel. The first row stored is the bottom of the picture, so the
  /// picture's top row is the last one written and reads back as <c>height - 1</c>.
  /// </summary>
  private static byte[] _Build(int width, int height) {
    using var ms = new MemoryStream();

    _WriteUInt16(ms, PcPaintFile.Magic);
    _WriteUInt16(ms, width);
    _WriteUInt16(ms, height);
    _WriteUInt16(ms, 0);
    _WriteUInt16(ms, 0);
    ms.WriteByte(8);
    ms.WriteByte(PcPaintFile.VersionTwoFlag);
    ms.WriteByte((byte)'A');
    _WriteUInt16(ms, PcPaintFile.PaletteVga);
    _WriteUInt16(ms, PcPaintFile.VgaPaletteBytes);
    ms.Write(new byte[PcPaintFile.VgaPaletteBytes], 0, PcPaintFile.VgaPaletteBytes);

    var body = new byte[width * height];
    for (var row = 0; row < height; ++row)
      for (var x = 0; x < width; ++x)
        body[row * width + x] = (byte)row;

    _WriteUInt16(ms, 1);
    _WriteUInt16(ms, PcPaintFile.BlockHeaderSize + body.Length);
    _WriteUInt16(ms, body.Length);
    ms.WriteByte(0xFE);
    ms.Write(body, 0, body.Length);

    return ms.ToArray();
  }

  private static void _WriteUInt16(MemoryStream ms, int value) {
    ms.WriteByte((byte)(value & 0xFF));
    ms.WriteByte((byte)((value >> 8) & 0xFF));
  }
}
