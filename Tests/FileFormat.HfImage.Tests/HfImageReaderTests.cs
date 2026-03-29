using System;
using System.IO;
using FileFormat.HfImage;

namespace FileFormat.HfImage.Tests;

[TestFixture]
public sealed class HfImageReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HfImageReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HfImageReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hf"));
    Assert.Throws<FileNotFoundException>(() => HfImageReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HfImageReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => HfImageReader.FromBytes(new byte[4]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[HfImageFile.HeaderSize + 8];
    Assert.Throws<InvalidDataException>(() => HfImageReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValid(16, 4);
    var result = HfImageReader.FromBytes(data);

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
    var file = HfImageReader.FromBytes(original);
    var written = HfImageWriter.ToBytes(file);
    var reread = HfImageReader.FromBytes(written);

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
    var result = HfImageReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
  }

  private static byte[] _BuildValid(int width, int height) {
    var pixelDataSize = width * height;
    var data = new byte[HfImageFile.HeaderSize + pixelDataSize];

    data[0] = 0x48; // 'H'
    data[1] = 0x46; // 'F'
    BitConverter.TryWriteBytes(new Span<byte>(data, 2, 2), (ushort)width);
    BitConverter.TryWriteBytes(new Span<byte>(data, 4, 2), (ushort)height);
    BitConverter.TryWriteBytes(new Span<byte>(data, 6, 2), (ushort)1); // dataType

    for (var i = 0; i < pixelDataSize; ++i)
      data[HfImageFile.HeaderSize + i] = (byte)(i % 256);

    return data;
  }
}
