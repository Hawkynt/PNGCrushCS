using System;
using System.IO;
using FileFormat.SunRaster;

namespace FileFormat.SunRaster.Tests;

[TestFixture]
public sealed class SunRasterReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SunRasterReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SunRasterReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ras"));
    Assert.Throws<FileNotFoundException>(() => SunRasterReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SunRasterReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => SunRasterReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[32];
    bad[0] = 0x00;
    bad[1] = 0x00;
    bad[2] = 0x00;
    bad[3] = 0x00;
    Assert.Throws<InvalidDataException>(() => SunRasterReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb24_ParsesCorrectly() {
    var data = _BuildMinimalRgb24SunRaster(4, 3);
    var result = SunRasterReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.Depth, Is.EqualTo(24));
    Assert.That(result.ColorMode, Is.EqualTo(SunRasterColorMode.Rgb24));
  }

  private static byte[] _BuildMinimalRgb24SunRaster(int width, int height) {
    var bytesPerRow = width * 3;
    var paddedBytesPerRow = (bytesPerRow + 1) & ~1;
    var pixelDataSize = paddedBytesPerRow * height;
    var fileSize = SunRasterHeader.StructSize + pixelDataSize;
    var data = new byte[fileSize];

    var header = new SunRasterHeader(
      Magic: SunRasterHeader.MagicValue,
      Width: width,
      Height: height,
      Depth: 24,
      Length: pixelDataSize,
      Type: 0,
      MapType: 0,
      MapLength: 0
    );
    header.WriteTo(data);

    for (var row = 0; row < height; ++row)
      for (var col = 0; col < width; ++col) {
        var offset = SunRasterHeader.StructSize + row * paddedBytesPerRow + col * 3;
        data[offset] = (byte)(row * 10);
        data[offset + 1] = (byte)(col * 20);
        data[offset + 2] = (byte)(row + col);
      }

    return data;
  }
}
