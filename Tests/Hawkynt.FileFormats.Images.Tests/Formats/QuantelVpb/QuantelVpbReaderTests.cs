using System;
using System.IO;
using FileFormat.QuantelVpb;

namespace FileFormat.QuantelVpb.Tests;

[TestFixture]
public sealed class QuantelVpbReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => QuantelVpbReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => QuantelVpbReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".vpb"));
    Assert.Throws<FileNotFoundException>(() => QuantelVpbReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => QuantelVpbReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => QuantelVpbReader.FromBytes(new byte[4]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[QuantelVpbFile.HeaderSize + 8];
    Assert.Throws<InvalidDataException>(() => QuantelVpbReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValid(4, 2);
    var result = QuantelVpbReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(4));
      Assert.That(result.Height, Is.EqualTo(2));
      Assert.That(result.PixelData.Length, Is.EqualTo(24));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValid(3, 2);
    var file = QuantelVpbReader.FromBytes(original);
    var written = QuantelVpbWriter.ToBytes(file);
    var reread = QuantelVpbReader.FromBytes(written);

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
    var result = QuantelVpbReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
  }

  private static byte[] _BuildValid(int width, int height) {
    var pixelDataSize = width * height * 3;
    var data = new byte[QuantelVpbFile.HeaderSize + pixelDataSize];

    data[0] = 0x51; // 'Q'
    data[1] = 0x56; // 'V'
    data[2] = 0x50; // 'P'
    data[3] = 0x42; // 'B'
    BitConverter.TryWriteBytes(new Span<byte>(data, 4, 2), (ushort)width);
    BitConverter.TryWriteBytes(new Span<byte>(data, 6, 2), (ushort)height);
    BitConverter.TryWriteBytes(new Span<byte>(data, 8, 2), (ushort)24); // bpp
    BitConverter.TryWriteBytes(new Span<byte>(data, 10, 2), (ushort)1); // fields
    // reserved = 0

    for (var i = 0; i < pixelDataSize; ++i)
      data[QuantelVpbFile.HeaderSize + i] = (byte)(i % 256);

    return data;
  }
}
