using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Dcx;
using FileFormat.Pcx;

namespace FileFormat.Dcx.Tests;

[TestFixture]
public sealed class DcxReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DcxReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DcxReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var file = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dcx"));
    Assert.Throws<FileNotFoundException>(() => DcxReader.FromFile(file));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var data = new byte[4];
    Assert.Throws<InvalidDataException>(() => DcxReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[8];
    BinaryPrimitives.WriteUInt32LittleEndian(data, 0xDEADBEEF);
    Assert.Throws<InvalidDataException>(() => DcxReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidSinglePage_ParsesCorrectly() {
    var pcx = _CreateSimplePcxFile();
    var pcxBytes = PcxWriter.ToBytes(pcx);

    var dcxBytes = _BuildDcxBytes(pcxBytes);
    var result = DcxReader.FromBytes(dcxBytes);

    Assert.That(result.Pages.Count, Is.EqualTo(1));
    Assert.That(result.Pages[0].Width, Is.EqualTo(pcx.Width));
    Assert.That(result.Pages[0].Height, Is.EqualTo(pcx.Height));
    Assert.That(result.Pages[0].ColorMode, Is.EqualTo(PcxColorMode.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidMultiPage_ParsesCorrectly() {
    var pcx1 = _CreateSimplePcxFile(4, 4);
    var pcx2 = _CreateSimplePcxFile(8, 8);
    var pcx1Bytes = PcxWriter.ToBytes(pcx1);
    var pcx2Bytes = PcxWriter.ToBytes(pcx2);

    var dcxBytes = _BuildDcxBytes(pcx1Bytes, pcx2Bytes);
    var result = DcxReader.FromBytes(dcxBytes);

    Assert.That(result.Pages.Count, Is.EqualTo(2));
    Assert.That(result.Pages[0].Width, Is.EqualTo(4));
    Assert.That(result.Pages[0].Height, Is.EqualTo(4));
    Assert.That(result.Pages[1].Width, Is.EqualTo(8));
    Assert.That(result.Pages[1].Height, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var pcx = _CreateSimplePcxFile();
    var pcxBytes = PcxWriter.ToBytes(pcx);
    var dcxBytes = _BuildDcxBytes(pcxBytes);
    using var ms = new MemoryStream(dcxBytes);

    var result = DcxReader.FromStream(ms);

    Assert.That(result.Pages.Count, Is.EqualTo(1));
    Assert.That(result.Pages[0].Width, Is.EqualTo(pcx.Width));
  }

  private static PcxFile _CreateSimplePcxFile(int width = 4, int height = 4) {
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7);

    return new PcxFile {
      Width = width,
      Height = height,
      BitsPerPixel = 8,
      PixelData = pixelData,
      ColorMode = PcxColorMode.Rgb24,
      PlaneConfig = PcxPlaneConfig.SeparatePlanes
    };
  }

  private static byte[] _BuildDcxBytes(params byte[][] pages) {
    // Header: magic (4) + offsets (pages.Length * 4) + zero terminator (4)
    var headerSize = 4 + (pages.Length + 1) * 4;
    var totalSize = headerSize;
    foreach (var page in pages)
      totalSize += page.Length;

    var result = new byte[totalSize];
    BinaryPrimitives.WriteUInt32LittleEndian(result, 0x3ADE68B1);

    var currentOffset = (uint)headerSize;
    for (var i = 0; i < pages.Length; ++i) {
      BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4 + i * 4), currentOffset);
      currentOffset += (uint)pages[i].Length;
    }

    // Zero terminator
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4 + pages.Length * 4), 0);

    // Write page data
    var offset = headerSize;
    foreach (var page in pages) {
      Array.Copy(page, 0, result, offset, page.Length);
      offset += page.Length;
    }

    return result;
  }
}
