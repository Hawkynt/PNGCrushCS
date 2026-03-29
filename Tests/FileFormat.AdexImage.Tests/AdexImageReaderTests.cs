using System;
using System.IO;
using FileFormat.AdexImage;

namespace FileFormat.AdexImage.Tests;

[TestFixture]
public sealed class AdexImageReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => AdexImageReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => AdexImageReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".adx"));
    Assert.Throws<FileNotFoundException>(() => AdexImageReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => AdexImageReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => AdexImageReader.FromBytes(new byte[4]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[AdexImageFile.HeaderSize + 8];
    Assert.Throws<InvalidDataException>(() => AdexImageReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValid(16, 4, 24, 0);
    var result = AdexImageReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(16));
      Assert.That(result.Height, Is.EqualTo(4));
      Assert.That(result.Bpp, Is.EqualTo(24));
      Assert.That(result.Compression, Is.EqualTo(0));
      Assert.That(result.PixelData.Length, Is.EqualTo(16 * 4 * 3));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValid(8, 2, 24, 1);
    var file = AdexImageReader.FromBytes(original);
    var written = AdexImageWriter.ToBytes(file);
    var reread = AdexImageReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.Width, Is.EqualTo(file.Width));
      Assert.That(reread.Height, Is.EqualTo(file.Height));
      Assert.That(reread.Bpp, Is.EqualTo(file.Bpp));
      Assert.That(reread.Compression, Is.EqualTo(file.Compression));
      Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValid(8, 2, 24, 0);
    using var ms = new MemoryStream(data);
    var result = AdexImageReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
  }

  private static byte[] _BuildValid(int width, int height, ushort bpp, ushort compression) {
    var pixelDataSize = width * height * 3;
    var data = new byte[AdexImageFile.HeaderSize + pixelDataSize];

    data[0] = 0x41; // 'A'
    data[1] = 0x44; // 'D'
    data[2] = 0x45; // 'E'
    data[3] = 0x58; // 'X'
    BitConverter.TryWriteBytes(new Span<byte>(data, 4, 2), (ushort)width);
    BitConverter.TryWriteBytes(new Span<byte>(data, 6, 2), (ushort)height);
    BitConverter.TryWriteBytes(new Span<byte>(data, 8, 2), bpp);
    BitConverter.TryWriteBytes(new Span<byte>(data, 10, 2), compression);

    for (var i = 0; i < pixelDataSize; ++i)
      data[AdexImageFile.HeaderSize + i] = (byte)(i % 256);

    return data;
  }
}
