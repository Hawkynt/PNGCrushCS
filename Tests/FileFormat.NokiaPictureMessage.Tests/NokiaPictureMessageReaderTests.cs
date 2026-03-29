using System;
using System.IO;
using FileFormat.NokiaPictureMessage;

namespace FileFormat.NokiaPictureMessage.Tests;

[TestFixture]
public sealed class NokiaPictureMessageReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NokiaPictureMessageReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NokiaPictureMessageReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".npm"));
    Assert.Throws<FileNotFoundException>(() => NokiaPictureMessageReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NokiaPictureMessageReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[3];
    Assert.Throws<InvalidDataException>(() => NokiaPictureMessageReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidType_ThrowsInvalidDataException() {
    // Type must be 0x00
    var data = new byte[] { 0x01, 8, 8, 0x01, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
    Assert.Throws<InvalidDataException>(() => NokiaPictureMessageReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidDepth_ThrowsInvalidDataException() {
    // Depth must be 0x01
    var data = new byte[] { 0x00, 8, 8, 0x02, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
    Assert.Throws<InvalidDataException>(() => NokiaPictureMessageReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroWidth_ThrowsInvalidDataException() {
    var data = new byte[] { 0x00, 0, 8, 0x01 };
    Assert.Throws<InvalidDataException>(() => NokiaPictureMessageReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroHeight_ThrowsInvalidDataException() {
    var data = new byte[] { 0x00, 8, 0, 0x01 };
    Assert.Throws<InvalidDataException>(() => NokiaPictureMessageReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TruncatedPixelData_ThrowsInvalidDataException() {
    // 8x2 = 1 byte/row * 2 rows = 2 bytes needed, but only 1 provided
    var data = new byte[] { 0x00, 8, 2, 0x01, 0xFF };
    Assert.Throws<InvalidDataException>(() => NokiaPictureMessageReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_72x28() {
    // Typical Nokia picture message: 72x28
    var bytesPerRow = (72 + 7) / 8; // 9
    var pixelBytes = bytesPerRow * 28; // 252
    var data = new byte[4 + pixelBytes];
    data[0] = 0x00;
    data[1] = 72;
    data[2] = 28;
    data[3] = 0x01;
    data[4] = 0xFF;

    var result = NokiaPictureMessageReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(72));
    Assert.That(result.Height, Is.EqualTo(28));
    Assert.That(result.PixelData.Length, Is.EqualTo(pixelBytes));
    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_8x2() {
    var data = new byte[] { 0x00, 8, 2, 0x01, 0xFF, 0xAA };

    var result = NokiaPictureMessageReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.PixelData.Length, Is.EqualTo(2));
    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(result.PixelData[1], Is.EqualTo(0xAA));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[] { 0x00, 8, 1, 0x01, 0xCD };

    using var ms = new MemoryStream(data);
    var result = NokiaPictureMessageReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.PixelData[0], Is.EqualTo(0xCD));
  }
}
