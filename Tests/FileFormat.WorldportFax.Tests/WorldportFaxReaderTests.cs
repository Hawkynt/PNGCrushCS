using System;
using System.IO;
using FileFormat.WorldportFax;

namespace FileFormat.WorldportFax.Tests;

[TestFixture]
public sealed class WorldportFaxReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => WorldportFaxReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => WorldportFaxReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wpf"));
    Assert.Throws<FileNotFoundException>(() => WorldportFaxReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => WorldportFaxReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => WorldportFaxReader.FromBytes(new byte[4]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[WorldportFaxFile.HeaderSize + 8];
    Assert.Throws<InvalidDataException>(() => WorldportFaxReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValidWpf(16, 4);
    var result = WorldportFaxReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(16));
      Assert.That(result.Height, Is.EqualTo(4));
      Assert.That(result.PixelData.Length, Is.EqualTo(8));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValidWpf(24, 3);
    var file = WorldportFaxReader.FromBytes(original);
    var written = WorldportFaxWriter.ToBytes(file);
    var reread = WorldportFaxReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.Width, Is.EqualTo(file.Width));
      Assert.That(reread.Height, Is.EqualTo(file.Height));
      Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValidWpf(8, 2);
    using var ms = new MemoryStream(data);
    var result = WorldportFaxReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
  }

  private static byte[] _BuildValidWpf(int width, int height) {
    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    var data = new byte[WorldportFaxFile.HeaderSize + pixelDataSize];

    data[0] = 0x57; // 'W'
    data[1] = 0x50; // 'P'
    data[2] = 0x46; // 'F'
    data[3] = 0x58; // 'X'
    BitConverter.TryWriteBytes(new Span<byte>(data, 4, 2), (ushort)width);
    BitConverter.TryWriteBytes(new Span<byte>(data, 6, 2), (ushort)height);
    // flags = 0

    for (var i = 0; i < pixelDataSize; ++i)
      data[WorldportFaxFile.HeaderSize + i] = (byte)(i % 256);

    return data;
  }
}
