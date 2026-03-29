using System;
using System.IO;
using FileFormat.PlotMaker;

namespace FileFormat.PlotMaker.Tests;

[TestFixture]
public sealed class PlotMakerReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PlotMakerReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PlotMakerReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".plt"));
    Assert.Throws<FileNotFoundException>(() => PlotMakerReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PlotMakerReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[2];
    Assert.Throws<InvalidDataException>(() => PlotMakerReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroWidth_ThrowsInvalidDataException() {
    var data = new byte[PlotMakerFile.HeaderSize];
    data[0] = 0;
    data[1] = 0;
    data[2] = 1;
    data[3] = 0;
    Assert.Throws<InvalidDataException>(() => PlotMakerReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroHeight_ThrowsInvalidDataException() {
    var data = new byte[PlotMakerFile.HeaderSize];
    data[0] = 1;
    data[1] = 0;
    data[2] = 0;
    data[3] = 0;
    Assert.Throws<InvalidDataException>(() => PlotMakerReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_HeaderOnly_TooSmallForPixels_ThrowsInvalidDataException() {
    // width=16, height=16 needs ceil(16/8)*16 = 32 bytes of pixel data
    var data = new byte[PlotMakerFile.HeaderSize + 10]; // not enough
    data[0] = 16;
    data[1] = 0;
    data[2] = 16;
    data[3] = 0;
    Assert.Throws<InvalidDataException>(() => PlotMakerReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesWidth() {
    var data = _BuildValid(16, 16);

    var result = PlotMakerReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesHeight() {
    var data = _BuildValid(16, 8);

    var result = PlotMakerReader.FromBytes(data);

    Assert.That(result.Height, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesPixelData() {
    var data = _BuildValid(16, 8);
    var pixelOffset = PlotMakerFile.HeaderSize;
    data[pixelOffset] = 0xAB;
    data[pixelOffset + 1] = 0xCD;

    var result = PlotMakerReader.FromBytes(data);

    var bytesPerRow = (16 + 7) / 8; // 2
    Assert.That(result.PixelData.Length, Is.EqualTo(bytesPerRow * 8));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
    Assert.That(result.PixelData[1], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid_Parses() {
    var data = _BuildValid(8, 4);

    using var ms = new MemoryStream(data);
    var result = PlotMakerReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NonByteBoundaryWidth_ParsesCorrectly() {
    // width=10 -> bytesPerRow = ceil(10/8) = 2
    var data = _BuildValid(10, 4);

    var result = PlotMakerReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(10));
    Assert.That(result.PixelData.Length, Is.EqualTo(2 * 4));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesPixelData_NotReference() {
    var data = _BuildValid(8, 1);
    data[PlotMakerFile.HeaderSize] = 0xFF;

    var result = PlotMakerReader.FromBytes(data);
    data[PlotMakerFile.HeaderSize] = 0x00;

    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
  }

  private static byte[] _BuildValid(int width, int height) {
    var bytesPerRow = (width + 7) / 8;
    var pixelBytes = bytesPerRow * height;
    var totalSize = PlotMakerFile.HeaderSize + pixelBytes;
    var data = new byte[totalSize];
    data[0] = (byte)(width & 0xFF);
    data[1] = (byte)(width >> 8);
    data[2] = (byte)(height & 0xFF);
    data[3] = (byte)(height >> 8);
    return data;
  }
}
