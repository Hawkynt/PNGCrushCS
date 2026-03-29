using System;
using System.IO;
using FileFormat.Ps2Txc;

namespace FileFormat.Ps2Txc.Tests;

[TestFixture]
public sealed class Ps2TxcReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Ps2TxcReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Ps2TxcReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txc"));
    Assert.Throws<FileNotFoundException>(() => Ps2TxcReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Ps2TxcReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => Ps2TxcReader.FromBytes(new byte[4]));

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidRgba32ParsesCorrectly() {
    var data = _BuildValidTxc(4, 2, 32);
    var result = Ps2TxcReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(4));
      Assert.That(result.Height, Is.EqualTo(2));
      Assert.That(result.BitsPerPixel, Is.EqualTo(32));
      Assert.That(result.PixelData.Length, Is.EqualTo(32));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidRgb24ParsesCorrectly() {
    var data = _BuildValidTxc(2, 2, 24);
    var result = Ps2TxcReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(2));
      Assert.That(result.Height, Is.EqualTo(2));
      Assert.That(result.BitsPerPixel, Is.EqualTo(24));
      Assert.That(result.PixelData.Length, Is.EqualTo(12));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValidTxc(3, 3, 32);
    var file = Ps2TxcReader.FromBytes(original);
    var written = Ps2TxcWriter.ToBytes(file);
    var reread = Ps2TxcReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.Width, Is.EqualTo(file.Width));
      Assert.That(reread.Height, Is.EqualTo(file.Height));
      Assert.That(reread.BitsPerPixel, Is.EqualTo(file.BitsPerPixel));
      Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValidTxc(2, 2, 32);
    using var ms = new MemoryStream(data);
    var result = Ps2TxcReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
  }

  private static byte[] _BuildValidTxc(int width, int height, int bpp) {
    var pixelDataSize = width * height * (bpp / 8);
    var data = new byte[Ps2TxcFile.HeaderSize + pixelDataSize];

    BitConverter.TryWriteBytes(new Span<byte>(data, 0, 2), (ushort)width);
    BitConverter.TryWriteBytes(new Span<byte>(data, 2, 2), (ushort)height);
    BitConverter.TryWriteBytes(new Span<byte>(data, 4, 2), (ushort)bpp);

    for (var i = 0; i < pixelDataSize; ++i)
      data[Ps2TxcFile.HeaderSize + i] = (byte)(i % 256);

    return data;
  }
}
