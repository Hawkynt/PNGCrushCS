using System;
using System.IO;
using FileFormat.Core;
using FileFormat.C128Hires;

namespace FileFormat.C128Hires.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var rawData = new byte[C128HiresFile.ExpectedFileSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 7 % 256);

    var original = new C128HiresFile { RawData = rawData };

    var bytes = C128HiresWriter.ToBytes(original);
    var restored = C128HiresReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new C128HiresFile {
      RawData = new byte[C128HiresFile.ExpectedFileSize]
    };

    var bytes = C128HiresWriter.ToBytes(original);
    var restored = C128HiresReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(320));
    Assert.That(restored.Height, Is.EqualTo(200));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllOnes() {
    var rawData = new byte[C128HiresFile.ExpectedFileSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = 0xFF;

    var original = new C128HiresFile { RawData = rawData };

    var bytes = C128HiresWriter.ToBytes(original);
    var restored = C128HiresReader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var rawData = new byte[C128HiresFile.ExpectedFileSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new C128HiresFile { RawData = rawData };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".c1h");
    try {
      var bytes = C128HiresWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = C128HiresReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.RawData, Is.EqualTo(original.RawData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var rawData = new byte[C128HiresFile.ExpectedFileSize];
    rawData[0] = 0xAA;
    rawData[7] = 0x55;
    rawData[8] = 0xCC;

    var original = new C128HiresFile { RawData = rawData };

    var raw = C128HiresFile.ToRawImage(original);
    var restored = C128HiresFile.FromRawImage(raw);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_AllZeros() {
    var original = new C128HiresFile {
      RawData = new byte[C128HiresFile.ExpectedFileSize]
    };

    var raw = C128HiresFile.ToRawImage(original);
    var restored = C128HiresFile.FromRawImage(raw);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_AllOnes() {
    var rawData = new byte[C128HiresFile.ExpectedFileSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = 0xFF;

    var original = new C128HiresFile { RawData = rawData };

    var raw = C128HiresFile.ToRawImage(original);
    var restored = C128HiresFile.FromRawImage(raw);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CellOrderConversion_FirstCell() {
    // Cell (0,0) row 0: first byte in cell-order = first byte in linear
    var rawData = new byte[C128HiresFile.ExpectedFileSize];
    rawData[0] = 0x80; // MSB = first pixel

    var file = new C128HiresFile { RawData = rawData };
    var raw = C128HiresFile.ToRawImage(file);

    // In linear order, cell(0,0) row 0 maps to scanline 0, byte 0
    Assert.That((raw.PixelData[0] & 0x80) != 0, Is.True, "First pixel in first cell should be set");

    var restored = C128HiresFile.FromRawImage(raw);
    Assert.That(restored.RawData[0], Is.EqualTo(0x80));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CellOrderConversion_SecondCellRow() {
    // Cell (0,0) row 1: rawData[1] maps to scanline 1, byte 0
    var rawData = new byte[C128HiresFile.ExpectedFileSize];
    rawData[1] = 0x80; // MSB = first pixel of second row in cell(0,0)

    var file = new C128HiresFile { RawData = rawData };
    var raw = C128HiresFile.ToRawImage(file);

    // Scanline 1, column 0
    var rowStride = C128HiresFile.PixelWidth / 8; // 40
    Assert.That((raw.PixelData[rowStride] & 0x80) != 0, Is.True, "First pixel of row 1 should be set");

    var restored = C128HiresFile.FromRawImage(raw);
    Assert.That(restored.RawData[1], Is.EqualTo(0x80));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CellOrderConversion_SecondColumn() {
    // Cell (1,0) row 0: rawData[8] maps to scanline 0, byte 1
    var rawData = new byte[C128HiresFile.ExpectedFileSize];
    rawData[8] = 0x80; // MSB = first pixel of cell(1,0)

    var file = new C128HiresFile { RawData = rawData };
    var raw = C128HiresFile.ToRawImage(file);

    // Scanline 0, column 1 byte
    Assert.That((raw.PixelData[1] & 0x80) != 0, Is.True, "First pixel of cell(1,0) should be set");

    var restored = C128HiresFile.FromRawImage(raw);
    Assert.That(restored.RawData[8], Is.EqualTo(0x80));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CellOrderConversion_SecondCellRow_SecondColumn() {
    // Cell (0,1) row 0: cellIndex = 1*40+0 = 40, rawData[40*8] = rawData[320]
    var rawData = new byte[C128HiresFile.ExpectedFileSize];
    rawData[320] = 0x80; // First pixel of cell row 1

    var file = new C128HiresFile { RawData = rawData };
    var raw = C128HiresFile.ToRawImage(file);

    // Scanline 8, column 0 (cell row 1, row 0)
    var rowStride = C128HiresFile.PixelWidth / 8;
    Assert.That((raw.PixelData[8 * rowStride] & 0x80) != 0, Is.True, "First pixel of cell(0,1) should be set");

    var restored = C128HiresFile.FromRawImage(raw);
    Assert.That(restored.RawData[320], Is.EqualTo(0x80));
  }
}
