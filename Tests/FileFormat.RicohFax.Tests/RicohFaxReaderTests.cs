using System;
using System.IO;
using FileFormat.RicohFax;

namespace FileFormat.RicohFax.Tests;

[TestFixture]
public sealed class RicohFaxReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => RicohFaxReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => RicohFaxReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ric"));
    Assert.Throws<FileNotFoundException>(() => RicohFaxReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => RicohFaxReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => RicohFaxReader.FromBytes(new byte[4]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[RicohFaxFile.HeaderSize + 8];
    Assert.Throws<InvalidDataException>(() => RicohFaxReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValidRic(16, 4);
    var result = RicohFaxReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(16));
      Assert.That(result.Height, Is.EqualTo(4));
      Assert.That(result.PixelData.Length, Is.EqualTo(8));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValidRic(24, 3);
    var file = RicohFaxReader.FromBytes(original);
    var written = RicohFaxWriter.ToBytes(file);
    var reread = RicohFaxReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.Width, Is.EqualTo(file.Width));
      Assert.That(reread.Height, Is.EqualTo(file.Height));
      Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValidRic(8, 2);
    using var ms = new MemoryStream(data);
    var result = RicohFaxReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
  }

  private static byte[] _BuildValidRic(int width, int height) {
    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    var data = new byte[RicohFaxFile.HeaderSize + pixelDataSize];

    data[0] = 0x52; // 'R'
    data[1] = 0x49; // 'I'
    data[2] = 0x43; // 'C'
    data[3] = 0x46; // 'F'
    BitConverter.TryWriteBytes(new Span<byte>(data, 4, 2), (ushort)width);
    BitConverter.TryWriteBytes(new Span<byte>(data, 6, 2), (ushort)height);
    // resolution = 0, compression = 0

    for (var i = 0; i < pixelDataSize; ++i)
      data[RicohFaxFile.HeaderSize + i] = (byte)(i % 256);

    return data;
  }
}
