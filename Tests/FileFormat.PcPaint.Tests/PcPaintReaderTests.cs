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
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => PcPaintReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[PcPaintFile.HeaderSize + PcPaintFile.PaletteSize];
    bad[0] = 0xFF;
    bad[1] = 0xFF;
    Assert.Throws<InvalidDataException>(() => PcPaintReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroWidth_ThrowsInvalidDataException() {
    var data = _BuildMinimalPcPaint(0, 2);
    Assert.Throws<InvalidDataException>(() => PcPaintReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroHeight_ThrowsInvalidDataException() {
    var data = _BuildMinimalPcPaint(2, 0);
    Assert.Throws<InvalidDataException>(() => PcPaintReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidPlanes_ThrowsInvalidDataException() {
    var data = _BuildMinimalPcPaint(2, 2);
    data[10] = 5;
    Assert.Throws<InvalidDataException>(() => PcPaintReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidBpp_ThrowsInvalidDataException() {
    var data = _BuildMinimalPcPaint(2, 2);
    data[11] = 3;
    Assert.Throws<InvalidDataException>(() => PcPaintReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesDimensions() {
    var data = _BuildMinimalPcPaint(4, 3);
    var result = PcPaintReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesPlanesAndBpp() {
    var data = _BuildMinimalPcPaint(2, 2);
    var result = PcPaintReader.FromBytes(data);

    Assert.That(result.Planes, Is.EqualTo(1));
    Assert.That(result.BitsPerPixel, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesPalette() {
    var data = _BuildMinimalPcPaint(2, 2);
    var paletteStart = PcPaintFile.HeaderSize;
    data[paletteStart] = 255;
    data[paletteStart + 1] = 128;
    data[paletteStart + 2] = 64;

    var result = PcPaintReader.FromBytes(data);

    Assert.That(result.Palette[0], Is.EqualTo(255));
    Assert.That(result.Palette[1], Is.EqualTo(128));
    Assert.That(result.Palette[2], Is.EqualTo(64));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesPixelData() {
    var data = _BuildMinimalPcPaint(2, 2);
    var pixelStart = PcPaintFile.HeaderSize + PcPaintFile.PaletteSize;
    data[pixelStart] = 1;
    data[pixelStart + 1] = 10;
    data[pixelStart + 2] = 1;
    data[pixelStart + 3] = 20;
    data[pixelStart + 4] = 1;
    data[pixelStart + 5] = 30;
    data[pixelStart + 6] = 1;
    data[pixelStart + 7] = 40;

    var result = PcPaintReader.FromBytes(data);

    Assert.That(result.PixelData.Length, Is.EqualTo(4));
    Assert.That(result.PixelData[0], Is.EqualTo(10));
    Assert.That(result.PixelData[1], Is.EqualTo(20));
    Assert.That(result.PixelData[2], Is.EqualTo(30));
    Assert.That(result.PixelData[3], Is.EqualTo(40));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesOffsets() {
    var data = _BuildMinimalPcPaint(2, 2);
    data[6] = 10;
    data[7] = 0;
    data[8] = 20;
    data[9] = 0;

    var result = PcPaintReader.FromBytes(data);

    Assert.That(result.XOffset, Is.EqualTo(10));
    Assert.That(result.YOffset, Is.EqualTo(20));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesAspect() {
    var data = _BuildMinimalPcPaint(2, 2);
    data[12] = 1;
    data[13] = 0;
    data[14] = 2;
    data[15] = 0;

    var result = PcPaintReader.FromBytes(data);

    Assert.That(result.XAspect, Is.EqualTo(1));
    Assert.That(result.YAspect, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid_ParsesCorrectly() {
    var data = _BuildMinimalPcPaint(3, 2);
    using var ms = new MemoryStream(data);
    var result = PcPaintReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(3));
    Assert.That(result.Height, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExtendedRun_DecodesCorrectly() {
    var width = 300;
    var height = 1;

    using var ms = new MemoryStream();

    _WriteUInt16(ms, PcPaintFile.Magic);
    _WriteUInt16(ms, (ushort)width);
    _WriteUInt16(ms, (ushort)height);
    _WriteUInt16(ms, 0);
    _WriteUInt16(ms, 0);
    ms.WriteByte(1);
    ms.WriteByte(8);
    _WriteUInt16(ms, 0);
    _WriteUInt16(ms, 0);
    _WriteUInt16(ms, PcPaintFile.PaletteSize);

    ms.Write(new byte[PcPaintFile.PaletteSize], 0, PcPaintFile.PaletteSize);

    ms.WriteByte(0);
    ms.WriteByte(42);
    _WriteUInt16(ms, (ushort)width);

    var data = ms.ToArray();
    var result = PcPaintReader.FromBytes(data);

    Assert.That(result.PixelData.Length, Is.EqualTo(width));
    for (var i = 0; i < width; ++i)
      Assert.That(result.PixelData[i], Is.EqualTo(42));
  }

  private static byte[] _BuildMinimalPcPaint(int width, int height) {
    var pixelCount = width * height;
    var rleSize = pixelCount * 2;
    var fileSize = PcPaintFile.HeaderSize + PcPaintFile.PaletteSize + rleSize;
    var data = new byte[fileSize];

    data[0] = (byte)(PcPaintFile.Magic & 0xFF);
    data[1] = (byte)((PcPaintFile.Magic >> 8) & 0xFF);
    data[2] = (byte)(width & 0xFF);
    data[3] = (byte)((width >> 8) & 0xFF);
    data[4] = (byte)(height & 0xFF);
    data[5] = (byte)((height >> 8) & 0xFF);
    data[6] = 0;
    data[7] = 0;
    data[8] = 0;
    data[9] = 0;
    data[10] = 1;
    data[11] = 8;
    data[12] = 0;
    data[13] = 0;
    data[14] = 0;
    data[15] = 0;
    data[16] = (byte)(PcPaintFile.PaletteSize & 0xFF);
    data[17] = (byte)((PcPaintFile.PaletteSize >> 8) & 0xFF);

    var rleOffset = PcPaintFile.HeaderSize + PcPaintFile.PaletteSize;
    for (var i = 0; i < pixelCount; ++i) {
      data[rleOffset + i * 2] = 1;
      data[rleOffset + i * 2 + 1] = 0;
    }

    return data;
  }

  private static void _WriteUInt16(MemoryStream ms, ushort value) {
    ms.WriteByte((byte)(value & 0xFF));
    ms.WriteByte((byte)((value >> 8) & 0xFF));
  }
}
