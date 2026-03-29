using System;
using System.Buffers.Binary;
using FileFormat.SyntheticArts;

namespace FileFormat.SyntheticArts.Tests;

[TestFixture]
public sealed class SyntheticArtsWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SyntheticArtsWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputIsExactly32032Bytes() {
    var file = _CreateMinimalFile();
    var bytes = SyntheticArtsWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(SyntheticArtsFile.FileSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesPaletteCorrectly() {
    var palette = new short[16];
    palette[0] = 0x0777;
    palette[1] = 0x0700;
    palette[2] = 0x0070;
    palette[3] = 0x0007;

    var file = new SyntheticArtsFile {
      Palette = palette,
      PixelData = new byte[32000]
    };

    var bytes = SyntheticArtsWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(0)), Is.EqualTo((short)0x0777));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(2)), Is.EqualTo((short)0x0700));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(4)), Is.EqualTo((short)0x0070));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(6)), Is.EqualTo((short)0x0007));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataAtOffset32() {
    var pixelData = new byte[32000];
    pixelData[0] = 0xAA;
    pixelData[1] = 0xBB;
    pixelData[31999] = 0xCC;

    var file = new SyntheticArtsFile {
      Palette = new short[16],
      PixelData = pixelData
    };

    var bytes = SyntheticArtsWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(bytes[32], Is.EqualTo(0xAA));
      Assert.That(bytes[33], Is.EqualTo(0xBB));
      Assert.That(bytes[32031], Is.EqualTo(0xCC));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_UnusedPaletteEntriesPreserved() {
    var palette = new short[16];
    palette[15] = 0x0123;

    var file = new SyntheticArtsFile {
      Palette = palette,
      PixelData = new byte[32000]
    };

    var bytes = SyntheticArtsWriter.ToBytes(file);
    Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(30)), Is.EqualTo((short)0x0123));
  }

  private static SyntheticArtsFile _CreateMinimalFile() => new() {
    Palette = new short[16],
    PixelData = new byte[32000]
  };
}
