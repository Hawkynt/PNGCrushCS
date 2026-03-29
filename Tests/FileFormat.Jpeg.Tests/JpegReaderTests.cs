using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using FileFormat.Jpeg;
using NUnit.Framework;

namespace FileFormat.Jpeg.Tests;

[TestFixture]
public sealed class JpegReaderTests {

  private static byte[] _CreateTestJpegBytes(int width = 4, int height = 4) {
    using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      bmp.SetPixel(x, y, Color.FromArgb(255, x * 60, y * 60, 128));

    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Jpeg);
    return ms.ToArray();
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => JpegReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => JpegReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => JpegReader.FromFile(new FileInfo("nonexistent_file_12345.jpg")));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => JpegReader.FromBytes(new byte[] { 0xFF }));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidSignature_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => JpegReader.FromBytes(new byte[] { 0x00, 0x00 }));

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidJpeg_ParsesCorrectly() {
    var jpegBytes = _CreateTestJpegBytes();

    var result = JpegReader.FromBytes(jpegBytes);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(4));
    Assert.That(result.IsGrayscale, Is.False);
    Assert.That(result.RawJpegBytes, Is.Not.Null);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidJpeg_HasRgbPixelData() {
    const int width = 4;
    const int height = 4;
    var jpegBytes = _CreateTestJpegBytes(width, height);

    var result = JpegReader.FromBytes(jpegBytes);

    Assert.That(result.RgbPixelData, Is.Not.Null);
    Assert.That(result.RgbPixelData!.Length, Is.EqualTo(width * height * 3));
  }
}
