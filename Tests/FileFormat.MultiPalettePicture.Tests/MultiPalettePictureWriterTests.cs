using System;
using System.Buffers.Binary;
using FileFormat.MultiPalettePicture;

namespace FileFormat.MultiPalettePicture.Tests;

[TestFixture]
public sealed class MultiPalettePictureWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MultiPalettePictureWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputIs38400Bytes() {
    var file = _BuildFile();
    var bytes = MultiPalettePictureWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(38400));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PreservesPaletteValues() {
    var file = _BuildFile();
    file.Palettes[0][0] = 0x0F00;
    file.Palettes[0][1] = 0x00F0;

    var bytes = MultiPalettePictureWriter.ToBytes(file);

    var paletteOffset = MultiPalettePictureFile.BytesPerScanline;
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(paletteOffset)), Is.EqualTo(0x0F00));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(paletteOffset + 2)), Is.EqualTo(0x00F0));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataAtCorrectOffsets() {
    var file = _BuildFile();
    file.PixelData[0] = 0xAB;
    file.PixelData[160] = 0xCD; // second scanline start

    var bytes = MultiPalettePictureWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(bytes[0], Is.EqualTo(0xAB));
      Assert.That(bytes[MultiPalettePictureFile.RecordSize], Is.EqualTo(0xCD));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SecondScanlinePaletteAtCorrectOffset() {
    var file = _BuildFile();
    file.Palettes[1][0] = 0x0ABC;

    var bytes = MultiPalettePictureWriter.ToBytes(file);

    var secondPaletteOffset = MultiPalettePictureFile.RecordSize + MultiPalettePictureFile.BytesPerScanline;
    Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(secondPaletteOffset)), Is.EqualTo(0x0ABC));
  }

  private static MultiPalettePictureFile _BuildFile() {
    var palettes = new short[200][];
    for (var y = 0; y < 200; ++y)
      palettes[y] = new short[16];

    return new MultiPalettePictureFile {
      PixelData = new byte[32000],
      Palettes = palettes
    };
  }
}
