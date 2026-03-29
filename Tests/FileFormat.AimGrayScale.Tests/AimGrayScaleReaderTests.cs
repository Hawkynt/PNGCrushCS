using System;
using System.IO;
using FileFormat.AimGrayScale;

namespace FileFormat.AimGrayScale.Tests;

[TestFixture]
public sealed class AimGrayScaleReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => AimGrayScaleReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => AimGrayScaleReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".aim"));
    Assert.Throws<FileNotFoundException>(() => AimGrayScaleReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => AimGrayScaleReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => AimGrayScaleReader.FromBytes(new byte[4]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[AimGrayScaleFile.HeaderSize + 8];
    Assert.Throws<InvalidDataException>(() => AimGrayScaleReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValid(16, 4);
    var result = AimGrayScaleReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(16));
      Assert.That(result.Height, Is.EqualTo(4));
      Assert.That(result.PixelData.Length, Is.EqualTo(16 * 4));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValid(8, 2);
    var file = AimGrayScaleReader.FromBytes(original);
    var written = AimGrayScaleWriter.ToBytes(file);
    var reread = AimGrayScaleReader.FromBytes(written);

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
    var result = AimGrayScaleReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
  }

  private static byte[] _BuildValid(int width, int height) {
    var pixelDataSize = width * height;
    var data = new byte[AimGrayScaleFile.HeaderSize + pixelDataSize];

    data[0] = 0x41; // 'A'
    data[1] = 0x49; // 'I'
    data[2] = 0x4D; // 'M'
    data[3] = 0x00; // '\0'
    BitConverter.TryWriteBytes(new Span<byte>(data, 4, 2), (ushort)width);
    BitConverter.TryWriteBytes(new Span<byte>(data, 6, 2), (ushort)height);

    for (var i = 0; i < pixelDataSize; ++i)
      data[AimGrayScaleFile.HeaderSize + i] = (byte)(i % 256);

    return data;
  }
}
