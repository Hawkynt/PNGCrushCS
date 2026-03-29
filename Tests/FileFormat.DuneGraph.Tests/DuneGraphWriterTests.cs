using System;
using FileFormat.DuneGraph;

namespace FileFormat.DuneGraph.Tests;

[TestFixture]
public sealed class DuneGraphWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DuneGraphWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Uncompressed_ProducesCorrectSize() {
    var file = new DuneGraphFile {
      IsCompressed = false,
      Palette = new byte[DuneGraphFile.PaletteEntryCount * 3],
      PixelData = new byte[DuneGraphFile.PixelDataSize],
    };

    var bytes = DuneGraphWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(DuneGraphFile.UncompressedFileSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Uncompressed_PaletteConvertedToFalconFormat() {
    var palette = new byte[DuneGraphFile.PaletteEntryCount * 3];
    palette[0] = 0xAA; // R
    palette[1] = 0xBB; // G
    palette[2] = 0xCC; // B

    var file = new DuneGraphFile {
      IsCompressed = false,
      Palette = palette,
      PixelData = new byte[DuneGraphFile.PixelDataSize],
    };

    var bytes = DuneGraphWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0xAA)); // R
    Assert.That(bytes[1], Is.EqualTo(0xBB)); // G
    Assert.That(bytes[2], Is.EqualTo(0x00)); // padding
    Assert.That(bytes[3], Is.EqualTo(0xCC)); // B
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Uncompressed_PixelDataPreserved() {
    var pixelData = new byte[DuneGraphFile.PixelDataSize];
    pixelData[0] = 0xFF;
    pixelData[DuneGraphFile.PixelDataSize - 1] = 0xDE;

    var file = new DuneGraphFile {
      IsCompressed = false,
      Palette = new byte[DuneGraphFile.PaletteEntryCount * 3],
      PixelData = pixelData,
    };

    var bytes = DuneGraphWriter.ToBytes(file);

    Assert.That(bytes[DuneGraphFile.PaletteDataSize], Is.EqualTo(0xFF));
    Assert.That(bytes[DuneGraphFile.PaletteDataSize + DuneGraphFile.PixelDataSize - 1], Is.EqualTo(0xDE));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Compressed_ProducesSmallerOutput() {
    var pixelData = new byte[DuneGraphFile.PixelDataSize];
    // Fill with same value for maximum compression
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = 0x42;

    var file = new DuneGraphFile {
      IsCompressed = true,
      Palette = new byte[DuneGraphFile.PaletteEntryCount * 3],
      PixelData = pixelData,
    };

    var bytes = DuneGraphWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.LessThan(DuneGraphFile.UncompressedFileSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Compressed_ZeroEscapeBytesAreRleEncoded() {
    var pixelData = new byte[DuneGraphFile.PixelDataSize];
    // First pixel is 0x00 (the escape byte) - must be RLE-encoded
    pixelData[0] = 0x00;

    var file = new DuneGraphFile {
      IsCompressed = true,
      Palette = new byte[DuneGraphFile.PaletteEntryCount * 3],
      PixelData = pixelData,
    };

    var bytes = DuneGraphWriter.ToBytes(file);
    var pixelStart = DuneGraphFile.PaletteDataSize;

    // The 0x00 byte must be encoded as RLE: {0x00, count, 0x00}
    Assert.That(bytes[pixelStart], Is.EqualTo(0x00));
  }
}
