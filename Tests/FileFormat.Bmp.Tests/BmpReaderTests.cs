using System;
using System.IO;
using FileFormat.Bmp;

namespace FileFormat.Bmp.Tests;

[TestFixture]
public sealed class BmpReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BmpReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BmpReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bmp"));
    Assert.Throws<FileNotFoundException>(() => BmpReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BmpReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => BmpReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidSignature_ThrowsInvalidDataException() {
    var bad = new byte[54];
    bad[0] = (byte)'X';
    bad[1] = (byte)'Y';
    Assert.Throws<InvalidDataException>(() => BmpReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb24_ParsesCorrectly() {
    var bmp = _BuildMinimalRgb24Bmp(2, 2);
    var result = BmpReader.FromBytes(bmp);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.BitsPerPixel, Is.EqualTo(24));
    Assert.That(result.ColorMode, Is.EqualTo(BmpColorMode.Rgb24));
  }

  private static byte[] _BuildMinimalRgb24Bmp(int width, int height) {
    var bytesPerRow = width * 3;
    var paddedBytesPerRow = (bytesPerRow + 3) & ~3;
    var pixelDataSize = paddedBytesPerRow * height;
    var fileSize = 54 + pixelDataSize;
    var data = new byte[fileSize];

    using var ms = new MemoryStream(data);
    using var bw = new BinaryWriter(ms);

    // BITMAPFILEHEADER
    bw.Write((byte)'B');
    bw.Write((byte)'M');
    bw.Write(fileSize);
    bw.Write((short)0); // reserved1
    bw.Write((short)0); // reserved2
    bw.Write(54);       // pixel data offset

    // BITMAPINFOHEADER
    bw.Write(40);       // header size
    bw.Write(width);
    bw.Write(height);   // positive = bottom-up
    bw.Write((short)1); // planes
    bw.Write((short)24); // bitsPerPixel
    bw.Write(0);        // compression = BI_RGB
    bw.Write(pixelDataSize);
    bw.Write(2835);     // x pixels per meter
    bw.Write(2835);     // y pixels per meter
    bw.Write(0);        // colors used
    bw.Write(0);        // important colors

    // Pixel data: fill with a recognizable pattern (B,G,R per pixel)
    for (var row = 0; row < height; ++row) {
      for (var col = 0; col < width; ++col) {
        bw.Write((byte)(row * 10));       // B
        bw.Write((byte)(col * 20));       // G
        bw.Write((byte)(row + col));      // R
      }
      for (var pad = bytesPerRow; pad < paddedBytesPerRow; ++pad)
        bw.Write((byte)0);
    }

    return data;
  }
}
