using System;
using System.IO;
using FileFormat.Calamus;

namespace FileFormat.Calamus.Tests;

[TestFixture]
public sealed class CalamusReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CalamusReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CalamusReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cpi"));
    Assert.Throws<FileNotFoundException>(() => CalamusReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CalamusReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => CalamusReader.FromBytes(new byte[4]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[CalamusFile.MinFileSize + 12];
    Assert.Throws<InvalidDataException>(() => CalamusReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValid(32, 8);
    var result = CalamusReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(32));
      Assert.That(result.Height, Is.EqualTo(8));
      Assert.That(result.PixelData.Length, Is.EqualTo(4 * 8));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValid(64, 4);
    var file = CalamusReader.FromBytes(original);
    var written = CalamusWriter.ToBytes(file);
    var reread = CalamusReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.Width, Is.EqualTo(file.Width));
      Assert.That(reread.Height, Is.EqualTo(file.Height));
      Assert.That(reread.Version, Is.EqualTo(file.Version));
      Assert.That(reread.Bpp, Is.EqualTo(file.Bpp));
      Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValid(32, 8);
    using var ms = new MemoryStream(data);
    var result = CalamusReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(32));
  }

  private static byte[] _BuildValid(int width, int height) {
    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    var data = new byte[CalamusFile.HeaderSize + pixelDataSize];

    data[0] = 0x43; // 'C'
    data[1] = 0x41; // 'A'
    data[2] = 0x4C; // 'L'
    data[3] = 0x4D; // 'M'
    BitConverter.TryWriteBytes(new Span<byte>(data, 4, 2), (ushort)1); // version
    BitConverter.TryWriteBytes(new Span<byte>(data, 6, 2), (ushort)width);
    BitConverter.TryWriteBytes(new Span<byte>(data, 8, 2), (ushort)height);
    BitConverter.TryWriteBytes(new Span<byte>(data, 10, 2), (ushort)1); // bpp

    for (var i = 0; i < pixelDataSize; ++i)
      data[CalamusFile.HeaderSize + i] = (byte)(i % 256);

    return data;
  }
}
