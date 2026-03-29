using System;
using System.IO;
using FileFormat.MagicPainter;

namespace FileFormat.MagicPainter.Tests;

[TestFixture]
public sealed class MagicPainterReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MagicPainterReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MagicPainterReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mgp"));
    Assert.Throws<FileNotFoundException>(() => MagicPainterReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MagicPainterReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[3];
    Assert.Throws<InvalidDataException>(() => MagicPainterReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroWidth_ThrowsInvalidDataException() {
    var data = _CreateValidMgpData(0, 10, 2);
    Assert.Throws<InvalidDataException>(() => MagicPainterReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroHeight_ThrowsInvalidDataException() {
    var data = _CreateValidMgpData(10, 0, 2);
    Assert.Throws<InvalidDataException>(() => MagicPainterReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroPaletteCount_ThrowsInvalidDataException() {
    var data = _CreateValidMgpData(10, 10, 0);
    Assert.Throws<InvalidDataException>(() => MagicPainterReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PaletteCountTooLarge_ThrowsInvalidDataException() {
    var data = _CreateValidMgpData(10, 10, 257);
    Assert.Throws<InvalidDataException>(() => MagicPainterReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_DataTooSmallForPixels_ThrowsInvalidDataException() {
    // Header says 10x10 with 2 palette entries, but only give header + palette
    var data = new byte[6 + 6]; // header + 2 palette entries, missing pixel data
    data[0] = 10; data[1] = 0; // width=10
    data[2] = 10; data[3] = 0; // height=10
    data[4] = 2; data[5] = 0;  // paletteCount=2
    Assert.Throws<InvalidDataException>(() => MagicPainterReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesCorrectly() {
    var data = _CreateValidMgpData(8, 4, 4);
    var result = MagicPainterReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(4));
    Assert.That(result.PaletteCount, Is.EqualTo(4));
    Assert.That(result.Palette.Length, Is.EqualTo(12));
    Assert.That(result.PixelData.Length, Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LargeWidth_ParsesCorrectly() {
    var data = _CreateValidMgpData(320, 1, 2);
    var result = MagicPainterReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.PixelData.Length, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PaletteDataPreserved() {
    var data = _CreateValidMgpData(2, 2, 2);
    // Set palette entry 0 = (255, 0, 0), entry 1 = (0, 255, 0)
    data[6] = 255; data[7] = 0; data[8] = 0;
    data[9] = 0; data[10] = 255; data[11] = 0;

    var result = MagicPainterReader.FromBytes(data);

    Assert.That(result.Palette[0], Is.EqualTo(255));
    Assert.That(result.Palette[1], Is.EqualTo(0));
    Assert.That(result.Palette[2], Is.EqualTo(0));
    Assert.That(result.Palette[3], Is.EqualTo(0));
    Assert.That(result.Palette[4], Is.EqualTo(255));
    Assert.That(result.Palette[5], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = _CreateValidMgpData(8, 4, 4);
    using var stream = new MemoryStream(data);

    var result = MagicPainterReader.FromStream(stream);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(4));
  }

  private static byte[] _CreateValidMgpData(int width, int height, int paletteCount) {
    var paletteSize = paletteCount * 3;
    var pixelDataSize = width * height;
    var totalSize = 6 + paletteSize + pixelDataSize;
    var data = new byte[totalSize];

    data[0] = (byte)(width & 0xFF);
    data[1] = (byte)((width >> 8) & 0xFF);
    data[2] = (byte)(height & 0xFF);
    data[3] = (byte)((height >> 8) & 0xFF);
    data[4] = (byte)(paletteCount & 0xFF);
    data[5] = (byte)((paletteCount >> 8) & 0xFF);

    return data;
  }
}
