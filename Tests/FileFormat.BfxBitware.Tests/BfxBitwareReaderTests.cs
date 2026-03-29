using System;
using System.IO;
using FileFormat.BfxBitware;

namespace FileFormat.BfxBitware.Tests;

[TestFixture]
public sealed class BfxBitwareReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => BfxBitwareReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => BfxBitwareReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bfx"));
    Assert.Throws<FileNotFoundException>(() => BfxBitwareReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => BfxBitwareReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => BfxBitwareReader.FromBytes(new byte[4]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[BfxBitwareFile.HeaderSize + 8];
    Assert.Throws<InvalidDataException>(() => BfxBitwareReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValidBfx(16, 4);
    var result = BfxBitwareReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(16));
      Assert.That(result.Height, Is.EqualTo(4));
      Assert.That(result.PixelData.Length, Is.EqualTo(8));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValidBfx(24, 3);
    var file = BfxBitwareReader.FromBytes(original);
    var written = BfxBitwareWriter.ToBytes(file);
    var reread = BfxBitwareReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.Width, Is.EqualTo(file.Width));
      Assert.That(reread.Height, Is.EqualTo(file.Height));
      Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValidBfx(8, 2);
    using var ms = new MemoryStream(data);
    var result = BfxBitwareReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
  }

  private static byte[] _BuildValidBfx(int width, int height) {
    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    var data = new byte[BfxBitwareFile.HeaderSize + pixelDataSize];

    data[0] = 0x42; // 'B'
    data[1] = 0x46; // 'F'
    data[2] = 0x58; // 'X'
    data[3] = 0x00; // '\0'
    BitConverter.TryWriteBytes(new Span<byte>(data, 4, 2), (ushort)1); // version
    BitConverter.TryWriteBytes(new Span<byte>(data, 6, 2), (ushort)width);
    BitConverter.TryWriteBytes(new Span<byte>(data, 8, 2), (ushort)height);
    // compression = 0, reserved = 0

    for (var i = 0; i < pixelDataSize; ++i)
      data[BfxBitwareFile.HeaderSize + i] = (byte)(i % 256);

    return data;
  }
}
