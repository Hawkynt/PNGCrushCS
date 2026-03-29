using System;
using System.Buffers.Binary;
using FileFormat.HighresMedium;

namespace FileFormat.HighresMedium.Tests;

[TestFixture]
public sealed class HighresMediumWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HighresMediumWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputIsExactly64064Bytes() {
    var file = _CreateMinimalFile();
    var bytes = HighresMediumWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(HighresMediumFile.FileSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesFrame1PaletteCorrectly() {
    var palette1 = new short[16];
    palette1[0] = 0x0777;
    palette1[1] = 0x0700;

    var file = new HighresMediumFile {
      Palette1 = palette1,
      PixelData1 = new byte[32000],
      Palette2 = new short[16],
      PixelData2 = new byte[32000]
    };

    var bytes = HighresMediumWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(0)), Is.EqualTo((short)0x0777));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(2)), Is.EqualTo((short)0x0700));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesFrame2PaletteCorrectly() {
    var palette2 = new short[16];
    palette2[0] = 0x0070;
    palette2[1] = 0x0007;

    var file = new HighresMediumFile {
      Palette1 = new short[16],
      PixelData1 = new byte[32000],
      Palette2 = palette2,
      PixelData2 = new byte[32000]
    };

    var bytes = HighresMediumWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(32032)), Is.EqualTo((short)0x0070));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(32034)), Is.EqualTo((short)0x0007));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Frame1PixelDataAtOffset32() {
    var pixelData1 = new byte[32000];
    pixelData1[0] = 0xAA;
    pixelData1[31999] = 0xBB;

    var file = new HighresMediumFile {
      Palette1 = new short[16],
      PixelData1 = pixelData1,
      Palette2 = new short[16],
      PixelData2 = new byte[32000]
    };

    var bytes = HighresMediumWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(bytes[32], Is.EqualTo(0xAA));
      Assert.That(bytes[32031], Is.EqualTo(0xBB));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Frame2PixelDataAtOffset32064() {
    var pixelData2 = new byte[32000];
    pixelData2[0] = 0xCC;
    pixelData2[31999] = 0xDD;

    var file = new HighresMediumFile {
      Palette1 = new short[16],
      PixelData1 = new byte[32000],
      Palette2 = new short[16],
      PixelData2 = pixelData2
    };

    var bytes = HighresMediumWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(bytes[32032 + 32], Is.EqualTo(0xCC));
      Assert.That(bytes[32032 + 32 + 31999], Is.EqualTo(0xDD));
    });
  }

  private static HighresMediumFile _CreateMinimalFile() => new() {
    Palette1 = new short[16],
    PixelData1 = new byte[32000],
    Palette2 = new short[16],
    PixelData2 = new byte[32000]
  };
}
