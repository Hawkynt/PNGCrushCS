using System;
using System.Buffers.Binary;
using FileFormat.EzArt;

namespace FileFormat.EzArt.Tests;

[TestFixture]
public sealed class EzArtWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => EzArtWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ExactSize32032() {
    var file = _CreateMinimalFile();
    var bytes = EzArtWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(EzArtFile.FileSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesPaletteCorrectly() {
    var palette = new short[16];
    palette[0] = 0x0777;
    palette[1] = 0x0700;
    palette[15] = 0x0007;

    var file = new EzArtFile {
      Palette = palette,
      PixelData = new byte[32000]
    };

    var bytes = EzArtWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(0)), Is.EqualTo((short)0x0777));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(2)), Is.EqualTo((short)0x0700));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(30)), Is.EqualTo((short)0x0007));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataAtOffset32() {
    var pixelData = new byte[32000];
    pixelData[0] = 0xAA;
    pixelData[1] = 0xBB;
    pixelData[31999] = 0xCC;

    var file = new EzArtFile {
      Palette = new short[16],
      PixelData = pixelData
    };

    var bytes = EzArtWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(bytes[32], Is.EqualTo(0xAA));
      Assert.That(bytes[33], Is.EqualTo(0xBB));
      Assert.That(bytes[32031], Is.EqualTo(0xCC));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var file = _CreateMinimalFile();
    for (var i = 0; i < 32000; ++i)
      file.PixelData[i] = (byte)(i * 3 & 0xFF);

    var bytes = EzArtWriter.ToBytes(file);

    for (var i = 0; i < 32000; ++i)
      Assert.That(bytes[32 + i], Is.EqualTo((byte)(i * 3 & 0xFF)), $"Mismatch at pixel byte {i}");
  }

  private static EzArtFile _CreateMinimalFile() => new() {
    Palette = new short[16],
    PixelData = new byte[32000]
  };
}
