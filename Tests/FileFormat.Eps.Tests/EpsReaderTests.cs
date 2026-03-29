using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Eps;

namespace FileFormat.Eps.Tests;

[TestFixture]
public sealed class EpsReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => EpsReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => EpsReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".eps"));
    Assert.Throws<FileNotFoundException>(() => EpsReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => EpsReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => EpsReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[30];
    data[0] = 0x00;
    data[1] = 0x00;
    data[2] = 0x00;
    data[3] = 0x00;
    Assert.Throws<InvalidDataException>(() => EpsReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NoTiffPreview_ThrowsInvalidDataException() {
    var data = new byte[30];
    data[0] = 0xC5;
    data[1] = 0xD0;
    data[2] = 0xD3;
    data[3] = 0xC6;
    // PS offset/length
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 30);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 0);
    // WMF = 0
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), 0);
    // TIFF offset/length = 0
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(24), 0);

    Assert.Throws<InvalidDataException>(() => EpsReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidWithTiffPreview_ParsesCorrectly() {
    var original = new EpsFile {
      Width = 2,
      Height = 2,
      PixelData = _CreateRgb24PixelData(2, 2)
    };

    var bytes = EpsWriter.ToBytes(original);
    var result = EpsReader.FromBytes(bytes);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.PixelData.Length, Is.EqualTo(2 * 2 * 3));
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidEps_ParsesCorrectly() {
    var original = new EpsFile {
      Width = 2,
      Height = 1,
      PixelData = _CreateRgb24PixelData(2, 1)
    };

    var bytes = EpsWriter.ToBytes(original);
    using var stream = new MemoryStream(bytes);
    var result = EpsReader.FromStream(stream);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(1));
  }

  private static byte[] _CreateRgb24PixelData(int width, int height) {
    var data = new byte[width * height * 3];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 17 % 256);
    return data;
  }
}
