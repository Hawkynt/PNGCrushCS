using System;
using System.IO;
using FileFormat.Wsq;

namespace FileFormat.Wsq.Tests;

[TestFixture]
public sealed class WsqReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WsqReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WsqReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wsq"));
    Assert.Throws<FileNotFoundException>(() => WsqReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WsqReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[4];
    Assert.Throws<InvalidDataException>(() => WsqReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[20];
    data[0] = 0x89;
    data[1] = 0x50; // PNG signature, not WSQ
    Assert.Throws<InvalidDataException>(() => WsqReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidWsq_ParsesDimensions() {
    var original = new WsqFile {
      Width = 64,
      Height = 64,
      PixelData = _CreateGradient(64, 64)
    };

    var bytes = WsqWriter.ToBytes(original);
    var result = WsqReader.FromBytes(bytes);

    Assert.That(result.Width, Is.EqualTo(64));
    Assert.That(result.Height, Is.EqualTo(64));
    Assert.That(result.PixelData.Length, Is.EqualTo(64 * 64));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var original = new WsqFile {
      Width = 32,
      Height = 32,
      PixelData = _CreateGradient(32, 32)
    };

    var bytes = WsqWriter.ToBytes(original);
    using var ms = new MemoryStream(bytes);
    var result = WsqReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(32));
    Assert.That(result.Height, Is.EqualTo(32));
  }

  private static byte[] _CreateGradient(int width, int height) {
    var data = new byte[width * height];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      data[y * width + x] = (byte)((x + y) * 255 / (width + height - 2));
    return data;
  }
}
