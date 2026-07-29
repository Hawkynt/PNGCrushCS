using System;
using System.IO;
using FileFormat.DuneGraph;

namespace FileFormat.DuneGraph.Tests;

[TestFixture]
public sealed class DuneGraphReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DuneGraphReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DuneGraphReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dg1"));
    Assert.Throws<FileNotFoundException>(() => DuneGraphReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DuneGraphReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => DuneGraphReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_UncompressedExactSize_Parses() {
    var data = new byte[DuneGraphFile.UncompressedFileSize];
    DuneGraphFile.WriteHeader(data, DuneGraphFile.FixedWidth, DuneGraphFile.FixedHeight);
    // Set first palette entry: R=0xAA, G=0xBB, pad=0x00, B=0xCC
    data[DuneGraphFile.PaletteOffset + 0] = 0xAA;
    data[DuneGraphFile.PaletteOffset + 1] = 0xBB;
    data[DuneGraphFile.PaletteOffset + 2] = 0x00;
    data[DuneGraphFile.PaletteOffset + 3] = 0xCC;
    // Set first pixel index
    data[DuneGraphFile.PixelDataOffset] = 42;

    var result = DuneGraphReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.IsCompressed, Is.False);
    Assert.That(result.PixelData[0], Is.EqualTo(42));
    Assert.That(result.Palette[0], Is.EqualTo(0xAA));
    Assert.That(result.Palette[1], Is.EqualTo(0xBB));
    Assert.That(result.Palette[2], Is.EqualTo(0xCC));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CompressedData_Parses() {
    // Build a compressed file: palette + RLE-compressed pixel data
    var data = new byte[DuneGraphFile.PixelDataOffset + 3]; // header + palette + one RLE run
    DuneGraphFile.WriteHeader(data, DuneGraphFile.FixedWidth, DuneGraphFile.FixedHeight);
    // RLE: escape(0x00) + count(5) + value(0x42)
    data[DuneGraphFile.PixelDataOffset] = 0x00;
    data[DuneGraphFile.PixelDataOffset + 1] = 5;
    data[DuneGraphFile.PixelDataOffset + 2] = 0x42;

    var result = DuneGraphReader.FromBytes(data);

    Assert.That(result.IsCompressed, Is.True);
    Assert.That(result.PixelData[0], Is.EqualTo(0x42));
    Assert.That(result.PixelData[1], Is.EqualTo(0x42));
    Assert.That(result.PixelData[2], Is.EqualTo(0x42));
    Assert.That(result.PixelData[3], Is.EqualTo(0x42));
    Assert.That(result.PixelData[4], Is.EqualTo(0x42));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_UncompressedValid() {
    var data = new byte[DuneGraphFile.UncompressedFileSize];
    DuneGraphFile.WriteHeader(data, DuneGraphFile.FixedWidth, DuneGraphFile.FixedHeight);
    data[DuneGraphFile.PixelDataOffset] = 0xAB;

    using var ms = new MemoryStream(data);
    var result = DuneGraphReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesInputData() {
    var data = new byte[DuneGraphFile.UncompressedFileSize];
    DuneGraphFile.WriteHeader(data, DuneGraphFile.FixedWidth, DuneGraphFile.FixedHeight);
    data[DuneGraphFile.PixelDataOffset] = 0x42;

    var result = DuneGraphReader.FromBytes(data);
    data[DuneGraphFile.PixelDataOffset] = 0x00;

    Assert.That(result.PixelData[0], Is.EqualTo(0x42));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_FalconPaletteConversion_SkipsPaddingByte() {
    var data = new byte[DuneGraphFile.UncompressedFileSize];
    DuneGraphFile.WriteHeader(data, DuneGraphFile.FixedWidth, DuneGraphFile.FixedHeight);
    // Entry 0: R=0x10, G=0x20, pad=0xFF, B=0x30
    data[DuneGraphFile.PaletteOffset + 0] = 0x10;
    data[DuneGraphFile.PaletteOffset + 1] = 0x20;
    data[DuneGraphFile.PaletteOffset + 2] = 0xFF; // padding byte - should be ignored
    data[DuneGraphFile.PaletteOffset + 3] = 0x30;

    var result = DuneGraphReader.FromBytes(data);

    Assert.That(result.Palette[0], Is.EqualTo(0x10));
    Assert.That(result.Palette[1], Is.EqualTo(0x20));
    Assert.That(result.Palette[2], Is.EqualTo(0x30));
  }
}
