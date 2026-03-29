using System;
using FileFormat.AppleShr;

namespace FileFormat.AppleShr.Tests;

[TestFixture]
public sealed class AppleShrWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AppleShrWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputIsExactly32768Bytes() {
    var file = _BuildValidShrFile();
    var bytes = AppleShrWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(AppleShrFile.ExpectedFileSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataAtOffset0() {
    var file = new AppleShrFile {
      PixelData = new byte[32000],
      ScanlineControl = new byte[200],
      Palette = new byte[512]
    };
    file.PixelData[0] = 0xAA;
    file.PixelData[31999] = 0xBB;

    var bytes = AppleShrWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0xAA));
    Assert.That(bytes[31999], Is.EqualTo(0xBB));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ScanlineControlAtOffset32000() {
    var file = new AppleShrFile {
      PixelData = new byte[32000],
      ScanlineControl = new byte[200],
      Palette = new byte[512]
    };
    file.ScanlineControl[0] = 0x05;
    file.ScanlineControl[199] = 0x0F;

    var bytes = AppleShrWriter.ToBytes(file);

    Assert.That(bytes[32000], Is.EqualTo(0x05));
    Assert.That(bytes[32199], Is.EqualTo(0x0F));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PaddingRegionIsZeros() {
    var file = _BuildValidShrFile();

    var bytes = AppleShrWriter.ToBytes(file);

    // Padding is at offset 32200..32255 (56 bytes)
    for (var i = 32200; i < 32256; ++i)
      Assert.That(bytes[i], Is.EqualTo(0x00), $"Padding byte at offset {i} should be zero");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PaletteAtOffset32256() {
    var file = new AppleShrFile {
      PixelData = new byte[32000],
      ScanlineControl = new byte[200],
      Palette = new byte[512]
    };
    file.Palette[0] = 0x12;
    file.Palette[511] = 0x34;

    var bytes = AppleShrWriter.ToBytes(file);

    Assert.That(bytes[32256], Is.EqualTo(0x12));
    Assert.That(bytes[32767], Is.EqualTo(0x34));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileLayout_SectionsDoNotOverlap() {
    var file = new AppleShrFile {
      PixelData = new byte[32000],
      ScanlineControl = new byte[200],
      Palette = new byte[512]
    };

    // Fill each section with different values
    Array.Fill(file.PixelData, (byte)0x11);
    Array.Fill(file.ScanlineControl, (byte)0x22);
    Array.Fill(file.Palette, (byte)0x33);

    var bytes = AppleShrWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x11));
    Assert.That(bytes[31999], Is.EqualTo(0x11));
    Assert.That(bytes[32000], Is.EqualTo(0x22));
    Assert.That(bytes[32199], Is.EqualTo(0x22));
    Assert.That(bytes[32256], Is.EqualTo(0x33));
    Assert.That(bytes[32767], Is.EqualTo(0x33));
  }

  private static AppleShrFile _BuildValidShrFile() {
    var pixelData = new byte[32000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var scb = new byte[200];
    for (var i = 0; i < scb.Length; ++i)
      scb[i] = (byte)(i % 16);

    var palette = new byte[512];
    for (var i = 0; i < palette.Length; ++i)
      palette[i] = (byte)(i % 256);

    return new() {
      PixelData = pixelData,
      ScanlineControl = scb,
      Palette = palette
    };
  }
}
