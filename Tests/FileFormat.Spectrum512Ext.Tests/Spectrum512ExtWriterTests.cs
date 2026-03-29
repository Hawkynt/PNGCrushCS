using System;
using System.Buffers.Binary;
using FileFormat.Spectrum512Ext;

namespace FileFormat.Spectrum512Ext.Tests;

[TestFixture]
public sealed class Spectrum512ExtWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Spectrum512ExtWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ExactSize51104() {
    var file = _BuildFile();
    var bytes = Spectrum512ExtWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(Spectrum512ExtFile.FileSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var file = _BuildFile();
    for (var i = 0; i < 32000; ++i)
      file.PixelData[i] = (byte)(i * 3 & 0xFF);

    var bytes = Spectrum512ExtWriter.ToBytes(file);

    for (var i = 0; i < 32000; ++i)
      Assert.That(bytes[i], Is.EqualTo((byte)(i * 3 & 0xFF)), $"Mismatch at pixel byte {i}");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PaletteDataPreserved() {
    var file = _BuildFile();
    file.Palettes[0][0] = 0x0FFF;
    file.Palettes[0][1] = 0x0F00;
    file.Palettes[198][47] = 0x000F;

    var bytes = Spectrum512ExtWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(32000)), Is.EqualTo(0x0FFF));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(32002)), Is.EqualTo(0x0F00));
      var lastOffset = 32000 + (198 * 48 + 47) * 2;
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(lastOffset)), Is.EqualTo(0x000F));
    });
  }

  private static Spectrum512ExtFile _BuildFile() {
    var palettes = new short[Spectrum512ExtFile.ScanlineCount][];
    for (var i = 0; i < Spectrum512ExtFile.ScanlineCount; ++i)
      palettes[i] = new short[Spectrum512ExtFile.PaletteEntriesPerLine];

    return new Spectrum512ExtFile {
      Width = 320,
      Height = 199,
      PixelData = new byte[32000],
      Palettes = palettes
    };
  }
}
