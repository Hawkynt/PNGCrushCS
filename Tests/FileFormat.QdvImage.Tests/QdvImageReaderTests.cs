using System;
using System.IO;
using FileFormat.QdvImage;

namespace FileFormat.QdvImage.Tests;

[TestFixture]
public sealed class QdvImageReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => QdvImageReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => QdvImageReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".qdv"));
    Assert.Throws<FileNotFoundException>(() => QdvImageReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => QdvImageReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => QdvImageReader.FromBytes(new byte[4]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[QdvImageFile.HeaderSize + 8];
    Assert.Throws<InvalidDataException>(() => QdvImageReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValid(16, 4, 24, 0);
    var result = QdvImageReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(16));
      Assert.That(result.Height, Is.EqualTo(4));
      Assert.That(result.Bpp, Is.EqualTo(24));
      Assert.That(result.Flags, Is.EqualTo(0));
      Assert.That(result.PixelData.Length, Is.EqualTo(16 * 4 * 3));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValid(8, 2, 24, 5);
    var file = QdvImageReader.FromBytes(original);
    var written = QdvImageWriter.ToBytes(file);
    var reread = QdvImageReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.Width, Is.EqualTo(file.Width));
      Assert.That(reread.Height, Is.EqualTo(file.Height));
      Assert.That(reread.Bpp, Is.EqualTo(file.Bpp));
      Assert.That(reread.Flags, Is.EqualTo(file.Flags));
      Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValid(8, 2, 24, 0);
    using var ms = new MemoryStream(data);
    var result = QdvImageReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
  }

  private static byte[] _BuildValid(int width, int height, ushort bpp, ushort flags) {
    var pixelDataSize = width * height * 3;
    var data = new byte[QdvImageFile.HeaderSize + pixelDataSize];

    data[0] = 0x51; // 'Q'
    data[1] = 0x44; // 'D'
    data[2] = 0x56; // 'V'
    data[3] = 0x00; // '\0'
    BitConverter.TryWriteBytes(new Span<byte>(data, 4, 2), (ushort)width);
    BitConverter.TryWriteBytes(new Span<byte>(data, 6, 2), (ushort)height);
    BitConverter.TryWriteBytes(new Span<byte>(data, 8, 2), bpp);
    BitConverter.TryWriteBytes(new Span<byte>(data, 10, 2), flags);

    for (var i = 0; i < pixelDataSize; ++i)
      data[QdvImageFile.HeaderSize + i] = (byte)(i % 256);

    return data;
  }
}
