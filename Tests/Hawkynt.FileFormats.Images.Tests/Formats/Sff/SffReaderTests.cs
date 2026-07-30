using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Sff;

namespace FileFormat.Sff.Tests;

[TestFixture]
public sealed class SffReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SffReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SffReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var file = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sff"));
    Assert.Throws<FileNotFoundException>(() => SffReader.FromFile(file));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SffReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var data = new byte[8];
    Assert.Throws<InvalidDataException>(() => SffReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[SffHeader.StructSize];
    data[0] = 0xDE;
    data[1] = 0xAD;
    data[2] = 0xBE;
    data[3] = 0xEF;
    Assert.Throws<InvalidDataException>(() => SffReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidSinglePage_ParsesCorrectly() {
    var width = 16;
    var height = 4;
    var bytesPerRow = (width + 7) / 8; // 2
    var pixelDataLength = bytesPerRow * height; // 8
    var pixelData = new byte[pixelDataLength];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17);

    var sffBytes = _BuildSinglePageSff(width, height, pixelData);
    var result = SffReader.FromBytes(sffBytes);

    Assert.That(result.Version, Is.EqualTo(1));
    Assert.That(result.Pages.Count, Is.EqualTo(1));
    Assert.That(result.Pages[0].Width, Is.EqualTo(width));
    Assert.That(result.Pages[0].Height, Is.EqualTo(height));
    Assert.That(result.Pages[0].PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var width = 8;
    var height = 2;
    var pixelData = new byte[] { 0xFF, 0xAA };
    var sffBytes = _BuildSinglePageSff(width, height, pixelData);

    using var ms = new MemoryStream(sffBytes);
    var result = SffReader.FromStream(ms);

    Assert.That(result.Pages.Count, Is.EqualTo(1));
    Assert.That(result.Pages[0].Width, Is.EqualTo(width));
    Assert.That(result.Pages[0].PixelData, Is.EqualTo(pixelData));
  }

  private static byte[] _BuildSinglePageSff(int width, int height, byte[] pixelData) {
    var bytesPerRow = (width + 7) / 8;
    var pixelDataLength = bytesPerRow * height;
    var totalSize = SffHeader.StructSize + SffPageHeader.StructSize + pixelDataLength;
    var result = new byte[totalSize];
    var span = result.AsSpan();

    // File header
    var fileHeader = new SffHeader(
      SffHeader.MagicByte1,
      SffHeader.MagicByte2,
      SffHeader.MagicByte3,
      SffHeader.MagicByte4,
      1,
      0,
      0,
      1,
      (ushort)SffHeader.StructSize
    );
    fileHeader.WriteTo(span);

    // Page header
    var pageHeader = new SffPageHeader(
      (ushort)pixelDataLength,
      0,
      0,
      0,
      0,
      (ushort)width,
      (ushort)height,
      0,
      0
    );
    pageHeader.WriteTo(span[SffHeader.StructSize..]);

    // Pixel data
    Array.Copy(pixelData, 0, result, SffHeader.StructSize + SffPageHeader.StructSize, Math.Min(pixelData.Length, pixelDataLength));

    return result;
  }
}
