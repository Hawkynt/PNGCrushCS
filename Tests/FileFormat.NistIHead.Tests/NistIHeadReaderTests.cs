using System;
using System.IO;
using FileFormat.NistIHead;

namespace FileFormat.NistIHead.Tests;

[TestFixture]
public sealed class NistIHeadReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NistIHeadReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NistIHeadReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".nst"));
    Assert.Throws<FileNotFoundException>(() => NistIHeadReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NistIHeadReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => NistIHeadReader.FromBytes(new byte[4]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[NistIHeadFile.HeaderSize + 8];
    Assert.Throws<InvalidDataException>(() => NistIHeadReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValid(16, 4);
    var result = NistIHeadReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(16));
      Assert.That(result.Height, Is.EqualTo(4));
      Assert.That(result.PixelData.Length, Is.EqualTo(64));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValid(8, 3);
    var file = NistIHeadReader.FromBytes(original);
    var written = NistIHeadWriter.ToBytes(file);
    var reread = NistIHeadReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.Width, Is.EqualTo(file.Width));
      Assert.That(reread.Height, Is.EqualTo(file.Height));
      Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValid(8, 2);
    using var ms = new MemoryStream(data);
    var result = NistIHeadReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
  }

  private static byte[] _BuildValid(int width, int height) {
    var pixelDataSize = width * height;
    var data = new byte[NistIHeadFile.HeaderSize + pixelDataSize];

    data[0] = 0x4E; // 'N'
    data[1] = 0x49; // 'I'
    data[2] = 0x53; // 'S'
    data[3] = 0x54; // 'T'
    BitConverter.TryWriteBytes(new Span<byte>(data, 4, 2), (ushort)width);
    BitConverter.TryWriteBytes(new Span<byte>(data, 6, 2), (ushort)height);
    BitConverter.TryWriteBytes(new Span<byte>(data, 8, 2), (ushort)8); // bpp
    // compression = 0, reserved = 0

    for (var i = 0; i < pixelDataSize; ++i)
      data[NistIHeadFile.HeaderSize + i] = (byte)(i % 256);

    return data;
  }
}
