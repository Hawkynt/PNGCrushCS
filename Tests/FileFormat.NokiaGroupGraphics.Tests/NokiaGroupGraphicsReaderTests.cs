using System;
using System.IO;
using FileFormat.NokiaGroupGraphics;

namespace FileFormat.NokiaGroupGraphics.Tests;

[TestFixture]
public sealed class NokiaGroupGraphicsReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NokiaGroupGraphicsReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NokiaGroupGraphicsReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ngg"));
    Assert.Throws<FileNotFoundException>(() => NokiaGroupGraphicsReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NokiaGroupGraphicsReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => NokiaGroupGraphicsReader.FromBytes(new byte[3]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[NokiaGroupGraphicsFile.HeaderSize + 8];
    Assert.Throws<InvalidDataException>(() => NokiaGroupGraphicsReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValidNgg(16, 8);
    var result = NokiaGroupGraphicsReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(16));
      Assert.That(result.Height, Is.EqualTo(8));
      Assert.That(result.PixelData.Length, Is.EqualTo(16));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValidNgg(24, 4);
    var file = NokiaGroupGraphicsReader.FromBytes(original);
    var written = NokiaGroupGraphicsWriter.ToBytes(file);
    var reread = NokiaGroupGraphicsReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.Width, Is.EqualTo(file.Width));
      Assert.That(reread.Height, Is.EqualTo(file.Height));
      Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValidNgg(8, 4);
    using var ms = new MemoryStream(data);
    var result = NokiaGroupGraphicsReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
  }

  private static byte[] _BuildValidNgg(int width, int height) {
    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    var data = new byte[NokiaGroupGraphicsFile.HeaderSize + pixelDataSize];

    data[0] = 0x4E; // 'N'
    data[1] = 0x47; // 'G'
    data[2] = 0x47; // 'G'
    data[3] = 1;    // version
    data[4] = (byte)width;
    data[5] = (byte)height;

    for (var i = 0; i < pixelDataSize; ++i)
      data[NokiaGroupGraphicsFile.HeaderSize + i] = (byte)(i % 256);

    return data;
  }
}
