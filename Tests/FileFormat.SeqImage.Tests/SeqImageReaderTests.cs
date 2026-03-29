using System;
using System.IO;
using FileFormat.SeqImage;

namespace FileFormat.SeqImage.Tests;

[TestFixture]
public sealed class SeqImageReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SeqImageReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SeqImageReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".seq"));
    Assert.Throws<FileNotFoundException>(() => SeqImageReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SeqImageReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => SeqImageReader.FromBytes(new byte[4]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[SeqImageFile.HeaderSize + 8];
    Assert.Throws<InvalidDataException>(() => SeqImageReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValid(16, 4, 1, 5, 24);
    var result = SeqImageReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(16));
      Assert.That(result.Height, Is.EqualTo(4));
      Assert.That(result.Version, Is.EqualTo(1));
      Assert.That(result.FrameCount, Is.EqualTo(5));
      Assert.That(result.Bpp, Is.EqualTo(24));
      Assert.That(result.PixelData.Length, Is.EqualTo(16 * 4 * 3));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValid(8, 2, 2, 3, 24);
    var file = SeqImageReader.FromBytes(original);
    var written = SeqImageWriter.ToBytes(file);
    var reread = SeqImageReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.Width, Is.EqualTo(file.Width));
      Assert.That(reread.Height, Is.EqualTo(file.Height));
      Assert.That(reread.Version, Is.EqualTo(file.Version));
      Assert.That(reread.FrameCount, Is.EqualTo(file.FrameCount));
      Assert.That(reread.Bpp, Is.EqualTo(file.Bpp));
      Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValid(8, 2, 1, 1, 24);
    using var ms = new MemoryStream(data);
    var result = SeqImageReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
  }

  private static byte[] _BuildValid(int width, int height, ushort version, ushort frameCount, ushort bpp) {
    var pixelDataSize = width * height * 3;
    var data = new byte[SeqImageFile.HeaderSize + pixelDataSize];

    data[0] = 0x53; // 'S'
    data[1] = 0x45; // 'E'
    data[2] = 0x51; // 'Q'
    data[3] = 0x00; // '\0'
    BitConverter.TryWriteBytes(new Span<byte>(data, 4, 2), version);
    BitConverter.TryWriteBytes(new Span<byte>(data, 6, 2), (ushort)width);
    BitConverter.TryWriteBytes(new Span<byte>(data, 8, 2), (ushort)height);
    BitConverter.TryWriteBytes(new Span<byte>(data, 10, 2), frameCount);
    BitConverter.TryWriteBytes(new Span<byte>(data, 12, 2), bpp);
    // bytes 14-15 reserved

    for (var i = 0; i < pixelDataSize; ++i)
      data[SeqImageFile.HeaderSize + i] = (byte)(i % 256);

    return data;
  }
}
