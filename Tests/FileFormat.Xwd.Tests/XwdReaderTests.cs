using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Xwd;

namespace FileFormat.Xwd.Tests;

[TestFixture]
public sealed class XwdReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XwdReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XwdReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xwd"));
    Assert.Throws<FileNotFoundException>(() => XwdReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XwdReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[50];
    Assert.Throws<InvalidDataException>(() => XwdReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidVersion_ThrowsInvalidDataException() {
    var data = _BuildValidXwd(4, 2, 24, 0);
    // Patch version to 6 instead of 7
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), 6);
    Assert.Throws<InvalidDataException>(() => XwdReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid24bpp() {
    var width = 4;
    var height = 2;
    var bytesPerLine = width * 3;
    var pixelData = new byte[bytesPerLine * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i & 0xFF);

    var data = _BuildValidXwd(width, height, 24, 0, pixelData: pixelData);
    var result = XwdReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Height, Is.EqualTo(height));
    Assert.That(result.BitsPerPixel, Is.EqualTo(24));
    Assert.That(result.BytesPerLine, Is.EqualTo(bytesPerLine));
    Assert.That(result.PixelData.Length, Is.EqualTo(bytesPerLine * height));
    Assert.That(result.PixelData, Is.EqualTo(pixelData));
    Assert.That(result.Colormap, Is.Null);
  }

  private static byte[] _BuildValidXwd(
    int width,
    int height,
    int bitsPerPixel,
    int numColors,
    string windowName = "test",
    byte[]? pixelData = null,
    byte[]? colormap = null
  ) {
    var nameBytes = System.Text.Encoding.ASCII.GetBytes(windowName);
    var headerSize = (uint)(XwdHeader.StructSize + nameBytes.Length + 1);
    var bytesPerLine = width * (bitsPerPixel / 8);
    var colormapSize = numColors * 12;
    var pixelDataSize = bytesPerLine * height;

    pixelData ??= new byte[pixelDataSize];
    colormap ??= numColors > 0 ? new byte[colormapSize] : null;

    var totalSize = (int)headerSize + colormapSize + pixelDataSize;
    var result = new byte[totalSize];
    var span = result.AsSpan();

    var header = new XwdHeader(
      headerSize,
      7,
      (uint)XwdPixmapFormat.ZPixmap,
      (uint)bitsPerPixel,
      (uint)width,
      (uint)height,
      0,
      1,
      32,
      1,
      32,
      (uint)bitsPerPixel,
      (uint)bytesPerLine,
      (uint)XwdVisualClass.TrueColor,
      0x00FF0000,
      0x0000FF00,
      0x000000FF,
      8,
      (uint)numColors,
      (uint)numColors,
      (uint)width,
      (uint)height,
      0,
      0,
      0
    );
    header.WriteTo(span);

    Array.Copy(nameBytes, 0, result, XwdHeader.StructSize, nameBytes.Length);
    result[XwdHeader.StructSize + nameBytes.Length] = 0;

    if (colormap != null)
      Array.Copy(colormap, 0, result, (int)headerSize, colormap.Length);

    Array.Copy(pixelData, 0, result, (int)headerSize + colormapSize, Math.Min(pixelDataSize, pixelData.Length));

    return result;
  }
}
