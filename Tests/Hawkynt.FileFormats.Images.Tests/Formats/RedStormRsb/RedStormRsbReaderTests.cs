using System;
using System.IO;
using FileFormat.RedStormRsb;

namespace FileFormat.RedStormRsb.Tests;

[TestFixture]
public sealed class RedStormRsbReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => RedStormRsbReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => RedStormRsbReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".rsb"));
    Assert.Throws<FileNotFoundException>(() => RedStormRsbReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => RedStormRsbReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => RedStormRsbReader.FromBytes(new byte[4]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[RedStormRsbFile.MinFileSize + 12];
    Assert.Throws<InvalidDataException>(() => RedStormRsbReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValid(4, 2);
    var result = RedStormRsbReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(4));
      Assert.That(result.Height, Is.EqualTo(2));
      Assert.That(result.PixelData.Length, Is.EqualTo(4 * 2 * 3));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValid(8, 3);
    var file = RedStormRsbReader.FromBytes(original);
    var written = RedStormRsbWriter.ToBytes(file);
    var reread = RedStormRsbReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.Width, Is.EqualTo(file.Width));
      Assert.That(reread.Height, Is.EqualTo(file.Height));
      Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValid(4, 2);
    using var ms = new MemoryStream(data);
    var result = RedStormRsbReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(4));
  }

  private static byte[] _BuildValid(int width, int height) {
    var pixelDataSize = width * height * 3;
    var data = new byte[RedStormRsbFile.HeaderSize + pixelDataSize];

    data[0] = 0x52; // 'R'
    data[1] = 0x53; // 'S'
    data[2] = 0x42; // 'B'
    data[3] = 0x00; // '\0'
    BitConverter.TryWriteBytes(new Span<byte>(data, 4, 2), (ushort)1); // version
    BitConverter.TryWriteBytes(new Span<byte>(data, 6, 2), (ushort)width);
    BitConverter.TryWriteBytes(new Span<byte>(data, 8, 2), (ushort)height);
    BitConverter.TryWriteBytes(new Span<byte>(data, 10, 2), (ushort)24); // bpp

    for (var i = 0; i < pixelDataSize; ++i)
      data[RedStormRsbFile.HeaderSize + i] = (byte)(i % 256);

    return data;
  }
}
