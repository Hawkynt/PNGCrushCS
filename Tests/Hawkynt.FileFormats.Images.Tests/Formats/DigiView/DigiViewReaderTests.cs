using System;
using System.IO;
using FileFormat.DigiView;

namespace FileFormat.DigiView.Tests;

[TestFixture]
public sealed class DigiViewReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DigiViewReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DigiViewReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dgv"));
    Assert.Throws<FileNotFoundException>(() => DigiViewReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DigiViewReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[3];
    Assert.Throws<InvalidDataException>(() => DigiViewReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroWidth_ThrowsInvalidDataException() {
    var data = new byte[DigiViewFile.HeaderSize];
    data[0] = 0; // width BE high
    data[1] = 0; // width BE low
    data[2] = 0;
    data[3] = 1; // height = 1
    data[4] = 1; // channels
    Assert.Throws<InvalidDataException>(() => DigiViewReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroHeight_ThrowsInvalidDataException() {
    var data = new byte[DigiViewFile.HeaderSize];
    data[0] = 0;
    data[1] = 1; // width = 1
    data[2] = 0;
    data[3] = 0; // height = 0
    data[4] = 1; // channels
    Assert.Throws<InvalidDataException>(() => DigiViewReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidChannels_ThrowsInvalidDataException() {
    // channels=2 is invalid (only 1 or 3 allowed)
    var data = new byte[DigiViewFile.HeaderSize + 4]; // 2x2x1=4 would be enough
    data[0] = 0;
    data[1] = 2; // width = 2
    data[2] = 0;
    data[3] = 2; // height = 2
    data[4] = 2; // invalid channels
    Assert.Throws<InvalidDataException>(() => DigiViewReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmallForPixels_ThrowsInvalidDataException() {
    // width=4, height=4, channels=3 -> need 48 bytes of pixel data
    var data = new byte[DigiViewFile.HeaderSize + 10]; // not enough
    data[0] = 0;
    data[1] = 4; // width = 4
    data[2] = 0;
    data[3] = 4; // height = 4
    data[4] = 3; // channels = 3
    Assert.Throws<InvalidDataException>(() => DigiViewReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGrayscale_ParsesWidth() {
    var data = _BuildValidGray(16, 16);

    var result = DigiViewReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGrayscale_ParsesHeight() {
    var data = _BuildValidGray(16, 8);

    var result = DigiViewReader.FromBytes(data);

    Assert.That(result.Height, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGrayscale_ParsesChannels() {
    var data = _BuildValidGray(4, 4);

    var result = DigiViewReader.FromBytes(data);

    Assert.That(result.Channels, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGrayscale_ParsesPixelData() {
    var data = _BuildValidGray(4, 4);
    data[DigiViewFile.HeaderSize] = 0xAB;
    data[DigiViewFile.HeaderSize + 1] = 0xCD;

    var result = DigiViewReader.FromBytes(data);

    Assert.That(result.PixelData.Length, Is.EqualTo(16));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
    Assert.That(result.PixelData[1], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesChannels() {
    var data = _BuildValidRgb(4, 4);

    var result = DigiViewReader.FromBytes(data);

    Assert.That(result.Channels, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesPixelData() {
    var data = _BuildValidRgb(2, 2);

    var result = DigiViewReader.FromBytes(data);

    Assert.That(result.PixelData.Length, Is.EqualTo(2 * 2 * 3));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidGrayscale_Parses() {
    var data = _BuildValidGray(8, 4);

    using var ms = new MemoryStream(data);
    var result = DigiViewReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(4));
    Assert.That(result.Channels, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_LargeWidth_ParsesCorrectly() {
    var data = _BuildValidRgb(300, 1);

    var result = DigiViewReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(300));
    Assert.That(result.PixelData.Length, Is.EqualTo(300 * 3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesPixelData_NotReference() {
    var data = _BuildValidGray(4, 4);
    data[DigiViewFile.HeaderSize] = 0xFF;

    var result = DigiViewReader.FromBytes(data);
    data[DigiViewFile.HeaderSize] = 0x00;

    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
  }

  private static byte[] _BuildValidGray(int width, int height) {
    var pixelSize = width * height;
    var totalSize = DigiViewFile.HeaderSize + pixelSize;
    var data = new byte[totalSize];
    data[0] = (byte)(width >> 8);
    data[1] = (byte)(width & 0xFF);
    data[2] = (byte)(height >> 8);
    data[3] = (byte)(height & 0xFF);
    data[4] = 1; // grayscale
    return data;
  }

  private static byte[] _BuildValidRgb(int width, int height) {
    var pixelSize = width * height * 3;
    var totalSize = DigiViewFile.HeaderSize + pixelSize;
    var data = new byte[totalSize];
    data[0] = (byte)(width >> 8);
    data[1] = (byte)(width & 0xFF);
    data[2] = (byte)(height >> 8);
    data[3] = (byte)(height & 0xFF);
    data[4] = 3; // RGB
    return data;
  }
}
