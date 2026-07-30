using System;
using System.IO;
using FileFormat.G9b;

namespace FileFormat.G9b.Tests;

[TestFixture]
public sealed class G9bReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => G9bReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => G9bReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".g9b"));
    Assert.Throws<FileNotFoundException>(() => G9bReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => G9bReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[5];
    Assert.Throws<InvalidDataException>(() => G9bReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = _CreateValidG9bMode3(4, 4);
    data[0] = 0x00;
    Assert.Throws<InvalidDataException>(() => G9bReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroWidth_ThrowsInvalidDataException() {
    var data = _CreateValidG9bMode3(0, 4);
    Assert.Throws<InvalidDataException>(() => G9bReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroHeight_ThrowsInvalidDataException() {
    var data = _CreateValidG9bMode3(4, 0);
    Assert.Throws<InvalidDataException>(() => G9bReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_UnsupportedScreenMode_ThrowsInvalidDataException() {
    var data = _CreateValidG9bMode3(4, 4);
    data[5] = 7; // unsupported mode
    Assert.Throws<InvalidDataException>(() => G9bReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_DataTooSmallForPixels_ThrowsInvalidDataException() {
    // Create header only without enough pixel data
    var data = new byte[G9bReader.MinHeaderSize + 2]; // header + 2 bytes (not enough for 4x4)
    data[0] = 0x47; data[1] = 0x39; data[2] = 0x42; // G9B
    data[3] = 11; data[4] = 0; // header size
    data[5] = 3; // mode 3
    data[6] = 0; // color mode
    data[7] = 4; data[8] = 0; // width 4
    data[9] = 4; data[10] = 0; // height 4
    Assert.Throws<InvalidDataException>(() => G9bReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidMode3_ParsesCorrectly() {
    var data = _CreateValidG9bMode3(8, 4);
    var result = G9bReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(4));
    Assert.That(result.ScreenMode, Is.EqualTo(G9bScreenMode.Indexed8));
    Assert.That(result.PixelData.Length, Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidMode5_ParsesCorrectly() {
    var data = _CreateValidG9bMode5(8, 4);
    var result = G9bReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(4));
    Assert.That(result.ScreenMode, Is.EqualTo(G9bScreenMode.Rgb555));
    Assert.That(result.PixelData.Length, Is.EqualTo(64));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_HeaderSizePreserved() {
    var data = _CreateValidG9bMode3(4, 4);
    var result = G9bReader.FromBytes(data);

    Assert.That(result.HeaderSize, Is.EqualTo(G9bReader.DefaultHeaderSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelDataPreserved() {
    var data = _CreateValidG9bMode3(2, 2);
    data[11] = 0xAA;
    data[12] = 0xBB;
    data[13] = 0xCC;
    data[14] = 0xDD;

    var result = G9bReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(0xAA));
    Assert.That(result.PixelData[1], Is.EqualTo(0xBB));
    Assert.That(result.PixelData[2], Is.EqualTo(0xCC));
    Assert.That(result.PixelData[3], Is.EqualTo(0xDD));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = _CreateValidG9bMode3(8, 4);
    using var stream = new MemoryStream(data);

    var result = G9bReader.FromStream(stream);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_HeaderSizeTooSmall_ThrowsInvalidDataException() {
    var data = _CreateValidG9bMode3(4, 4);
    data[3] = 5; data[4] = 0; // header size = 5, less than minimum
    Assert.Throws<InvalidDataException>(() => G9bReader.FromBytes(data));
  }

  private static byte[] _CreateValidG9bMode3(int width, int height) {
    var pixelDataSize = width * height;
    var totalSize = G9bReader.DefaultHeaderSize + pixelDataSize;
    var data = new byte[totalSize];

    data[0] = 0x47; data[1] = 0x39; data[2] = 0x42; // G9B magic
    data[3] = (byte)(G9bReader.DefaultHeaderSize & 0xFF);
    data[4] = (byte)((G9bReader.DefaultHeaderSize >> 8) & 0xFF);
    data[5] = (byte)G9bScreenMode.Indexed8;
    data[6] = 0; // color mode
    data[7] = (byte)(width & 0xFF);
    data[8] = (byte)((width >> 8) & 0xFF);
    data[9] = (byte)(height & 0xFF);
    data[10] = (byte)((height >> 8) & 0xFF);

    return data;
  }

  private static byte[] _CreateValidG9bMode5(int width, int height) {
    var pixelDataSize = width * height * 2;
    var totalSize = G9bReader.DefaultHeaderSize + pixelDataSize;
    var data = new byte[totalSize];

    data[0] = 0x47; data[1] = 0x39; data[2] = 0x42; // G9B magic
    data[3] = (byte)(G9bReader.DefaultHeaderSize & 0xFF);
    data[4] = (byte)((G9bReader.DefaultHeaderSize >> 8) & 0xFF);
    data[5] = (byte)G9bScreenMode.Rgb555;
    data[6] = 0; // color mode
    data[7] = (byte)(width & 0xFF);
    data[8] = (byte)((width >> 8) & 0xFF);
    data[9] = (byte)(height & 0xFF);
    data[10] = (byte)((height >> 8) & 0xFF);

    return data;
  }
}
