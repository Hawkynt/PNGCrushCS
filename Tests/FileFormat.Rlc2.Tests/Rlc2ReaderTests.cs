using System;
using System.IO;
using FileFormat.Rlc2;

namespace FileFormat.Rlc2.Tests;

[TestFixture]
public sealed class Rlc2ReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Rlc2Reader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Rlc2Reader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".rlc"));
    Assert.Throws<FileNotFoundException>(() => Rlc2Reader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Rlc2Reader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => Rlc2Reader.FromBytes(new byte[4]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[Rlc2File.HeaderSize + 8];
    Assert.Throws<InvalidDataException>(() => Rlc2Reader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValid(16, 4, 24);
    var result = Rlc2Reader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(16));
      Assert.That(result.Height, Is.EqualTo(4));
      Assert.That(result.Bpp, Is.EqualTo(24));
      Assert.That(result.PixelData.Length, Is.EqualTo(16 * 4 * 3));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValid(8, 2, 24);
    var file = Rlc2Reader.FromBytes(original);
    var written = Rlc2Writer.ToBytes(file);
    var reread = Rlc2Reader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.Width, Is.EqualTo(file.Width));
      Assert.That(reread.Height, Is.EqualTo(file.Height));
      Assert.That(reread.Bpp, Is.EqualTo(file.Bpp));
      Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValid(8, 2, 24);
    using var ms = new MemoryStream(data);
    var result = Rlc2Reader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
  }

  private static byte[] _BuildValid(int width, int height, ushort bpp) {
    var pixelDataSize = width * height * 3;
    var data = new byte[Rlc2File.HeaderSize + pixelDataSize];

    data[0] = 0x52; // 'R'
    data[1] = 0x4C; // 'L'
    data[2] = 0x43; // 'C'
    data[3] = 0x32; // '2'
    BitConverter.TryWriteBytes(new Span<byte>(data, 4, 2), (ushort)width);
    BitConverter.TryWriteBytes(new Span<byte>(data, 6, 2), (ushort)height);
    BitConverter.TryWriteBytes(new Span<byte>(data, 8, 2), bpp);

    for (var i = 0; i < pixelDataSize; ++i)
      data[Rlc2File.HeaderSize + i] = (byte)(i % 256);

    return data;
  }
}
