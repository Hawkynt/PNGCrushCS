using System;
using System.IO;
using FileFormat.Cel;

namespace FileFormat.Cel.Tests;

[TestFixture]
public sealed class CelReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CelReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CelReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cel"));
    Assert.Throws<FileNotFoundException>(() => CelReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CelReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[31];
    Assert.Throws<InvalidDataException>(() => CelReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[32];
    data[0] = 0x00;
    data[1] = 0x00;
    data[2] = 0x00;
    data[3] = 0x00;
    Assert.Throws<InvalidDataException>(() => CelReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_UnsupportedMark_ThrowsInvalidDataException() {
    var data = _BuildHeader(mark: 0xFF, bpp: 8, width: 2, height: 2);
    Assert.Throws<InvalidDataException>(() => CelReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidIndexed8() {
    var width = 4;
    var height = 2;
    var pixelData = new byte[width * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i + 1);

    var data = _BuildCel(mark: 0x04, bpp: 8, width: width, height: height, xOffset: 10, yOffset: 20, pixelData: pixelData);

    var result = CelReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Height, Is.EqualTo(height));
    Assert.That(result.BitsPerPixel, Is.EqualTo(8));
    Assert.That(result.XOffset, Is.EqualTo(10));
    Assert.That(result.YOffset, Is.EqualTo(20));
    Assert.That(result.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidIndexed4() {
    var width = 4;
    var height = 2;
    var packedSize = ((width + 1) / 2) * height;
    var pixelData = new byte[packedSize];
    pixelData[0] = 0x12;
    pixelData[1] = 0x34;
    pixelData[2] = 0x56;
    pixelData[3] = 0x78;

    var data = _BuildCel(mark: 0x04, bpp: 4, width: width, height: height, xOffset: 0, yOffset: 0, pixelData: pixelData);

    var result = CelReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Height, Is.EqualTo(height));
    Assert.That(result.BitsPerPixel, Is.EqualTo(4));
    Assert.That(result.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgba32() {
    var width = 2;
    var height = 2;
    var pixelData = new byte[width * height * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var data = _BuildCel(mark: 0x20, bpp: 32, width: width, height: height, xOffset: 5, yOffset: 15, pixelData: pixelData);

    var result = CelReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Height, Is.EqualTo(height));
    Assert.That(result.BitsPerPixel, Is.EqualTo(32));
    Assert.That(result.XOffset, Is.EqualTo(5));
    Assert.That(result.YOffset, Is.EqualTo(15));
    Assert.That(result.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TruncatedPixelData_ThrowsInvalidDataException() {
    var data = _BuildHeader(mark: 0x04, bpp: 8, width: 10, height: 10);
    Assert.Throws<InvalidDataException>(() => CelReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidRgba32() {
    var width = 2;
    var height = 1;
    var pixelData = new byte[width * height * 4];
    pixelData[0] = 0xFF;
    pixelData[7] = 0xAA;

    var data = _BuildCel(mark: 0x20, bpp: 32, width: width, height: height, xOffset: 0, yOffset: 0, pixelData: pixelData);

    using var ms = new MemoryStream(data);
    var result = CelReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.BitsPerPixel, Is.EqualTo(32));
    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(result.PixelData[7], Is.EqualTo(0xAA));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroWidth_ThrowsInvalidDataException() {
    var data = _BuildHeader(mark: 0x04, bpp: 8, width: 0, height: 1);
    Assert.Throws<InvalidDataException>(() => CelReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroHeight_ThrowsInvalidDataException() {
    var data = _BuildHeader(mark: 0x04, bpp: 8, width: 1, height: 0);
    Assert.Throws<InvalidDataException>(() => CelReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidBppForIndexed_ThrowsInvalidDataException() {
    var data = _BuildHeader(mark: 0x04, bpp: 16, width: 2, height: 2);
    var padded = new byte[data.Length + 100];
    Array.Copy(data, padded, data.Length);
    Assert.Throws<InvalidDataException>(() => CelReader.FromBytes(padded));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidBppForRgba_ThrowsInvalidDataException() {
    var data = _BuildHeader(mark: 0x20, bpp: 8, width: 2, height: 2);
    var padded = new byte[data.Length + 100];
    Array.Copy(data, padded, data.Length);
    Assert.Throws<InvalidDataException>(() => CelReader.FromBytes(padded));
  }

  private static byte[] _BuildHeader(byte mark, byte bpp, int width, int height, int xOffset = 0, int yOffset = 0) {
    var data = new byte[32];
    data[0] = (byte)'K';
    data[1] = (byte)'i';
    data[2] = (byte)'S';
    data[3] = (byte)'S';
    data[4] = mark;
    data[5] = bpp;
    BitConverter.GetBytes((uint)width).CopyTo(data, 8);
    BitConverter.GetBytes((uint)height).CopyTo(data, 12);
    BitConverter.GetBytes((uint)xOffset).CopyTo(data, 16);
    BitConverter.GetBytes((uint)yOffset).CopyTo(data, 20);
    return data;
  }

  private static byte[] _BuildCel(byte mark, byte bpp, int width, int height, int xOffset, int yOffset, byte[] pixelData) {
    var header = _BuildHeader(mark, bpp, width, height, xOffset, yOffset);
    var result = new byte[header.Length + pixelData.Length];
    Array.Copy(header, result, header.Length);
    Array.Copy(pixelData, 0, result, header.Length, pixelData.Length);
    return result;
  }
}
