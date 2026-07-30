using System;
using System.IO;
using FileFormat.EverexFax;

namespace FileFormat.EverexFax.Tests;

[TestFixture]
public sealed class EverexFaxReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => EverexFaxReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => EverexFaxReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".efx"));
    Assert.Throws<FileNotFoundException>(() => EverexFaxReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => EverexFaxReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => EverexFaxReader.FromBytes(new byte[4]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[EverexFaxFile.HeaderSize + 8];
    Assert.Throws<InvalidDataException>(() => EverexFaxReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValidEfx(16, 4);
    var result = EverexFaxReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(16));
      Assert.That(result.Height, Is.EqualTo(4));
      Assert.That(result.PixelData.Length, Is.EqualTo(8));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValidEfx(24, 3);
    var file = EverexFaxReader.FromBytes(original);
    var written = EverexFaxWriter.ToBytes(file);
    var reread = EverexFaxReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.Width, Is.EqualTo(file.Width));
      Assert.That(reread.Height, Is.EqualTo(file.Height));
      Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValidEfx(8, 2);
    using var ms = new MemoryStream(data);
    var result = EverexFaxReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
  }

  private static byte[] _BuildValidEfx(int width, int height) {
    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    var data = new byte[EverexFaxFile.HeaderSize + pixelDataSize];

    data[0] = 0x45; // 'E'
    data[1] = 0x46; // 'F'
    data[2] = 0x41; // 'A'
    data[3] = 0x58; // 'X'
    BitConverter.TryWriteBytes(new Span<byte>(data, 4, 2), (ushort)1); // version
    BitConverter.TryWriteBytes(new Span<byte>(data, 6, 2), (ushort)width);
    BitConverter.TryWriteBytes(new Span<byte>(data, 8, 2), (ushort)height);
    // pages = 0, compression = 0, reserved = 0

    for (var i = 0; i < pixelDataSize; ++i)
      data[EverexFaxFile.HeaderSize + i] = (byte)(i % 256);

    return data;
  }
}
