using System;
using System.IO;
using FileFormat.AvhrrImage;

namespace FileFormat.AvhrrImage.Tests;

[TestFixture]
public sealed class AvhrrImageReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => AvhrrImageReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => AvhrrImageReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sst"));
    Assert.Throws<FileNotFoundException>(() => AvhrrImageReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => AvhrrImageReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => AvhrrImageReader.FromBytes(new byte[4]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[AvhrrImageFile.HeaderSize + 8];
    Assert.Throws<InvalidDataException>(() => AvhrrImageReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValid(16, 4);
    var result = AvhrrImageReader.FromBytes(data);

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
    var file = AvhrrImageReader.FromBytes(original);
    var written = AvhrrImageWriter.ToBytes(file);
    var reread = AvhrrImageReader.FromBytes(written);

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
    var result = AvhrrImageReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
  }

  private static byte[] _BuildValid(int width, int height) {
    var pixelDataSize = width * height;
    var data = new byte[AvhrrImageFile.HeaderSize + pixelDataSize];

    data[0] = 0x41; // 'A'
    data[1] = 0x56; // 'V'
    data[2] = 0x48; // 'H'
    data[3] = 0x52; // 'R'
    BitConverter.TryWriteBytes(new Span<byte>(data, 4, 2), (ushort)width);
    BitConverter.TryWriteBytes(new Span<byte>(data, 6, 2), (ushort)height);
    BitConverter.TryWriteBytes(new Span<byte>(data, 8, 2), (ushort)1); // bands
    BitConverter.TryWriteBytes(new Span<byte>(data, 10, 2), (ushort)1); // dataType

    for (var i = 0; i < pixelDataSize; ++i)
      data[AvhrrImageFile.HeaderSize + i] = (byte)(i % 256);

    return data;
  }
}
