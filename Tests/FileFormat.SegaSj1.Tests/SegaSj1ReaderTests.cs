using System;
using System.IO;
using FileFormat.SegaSj1;

namespace FileFormat.SegaSj1.Tests;

[TestFixture]
public sealed class SegaSj1ReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SegaSj1Reader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SegaSj1Reader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sj1"));
    Assert.Throws<FileNotFoundException>(() => SegaSj1Reader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SegaSj1Reader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => SegaSj1Reader.FromBytes(new byte[4]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[SegaSj1File.MinFileSize + 12];
    Assert.Throws<InvalidDataException>(() => SegaSj1Reader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValid(4, 2);
    var result = SegaSj1Reader.FromBytes(data);

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
    var file = SegaSj1Reader.FromBytes(original);
    var written = SegaSj1Writer.ToBytes(file);
    var reread = SegaSj1Reader.FromBytes(written);

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
    var result = SegaSj1Reader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(4));
  }

  private static byte[] _BuildValid(int width, int height) {
    var pixelDataSize = width * height * 3;
    var data = new byte[SegaSj1File.HeaderSize + pixelDataSize];

    data[0] = 0x53; // 'S'
    data[1] = 0x4A; // 'J'
    data[2] = 0x31; // '1'
    data[3] = 0x00; // '\0'
    BitConverter.TryWriteBytes(new Span<byte>(data, 4, 2), (ushort)width);
    BitConverter.TryWriteBytes(new Span<byte>(data, 6, 2), (ushort)height);
    BitConverter.TryWriteBytes(new Span<byte>(data, 8, 2), (ushort)24); // bpp

    for (var i = 0; i < pixelDataSize; ++i)
      data[SegaSj1File.HeaderSize + i] = (byte)(i % 256);

    return data;
  }
}
