using System;
using System.Buffers.Binary;
using FileFormat.Neochrome;

namespace FileFormat.Neochrome.Tests;

[TestFixture]
public sealed class NeochromeWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputIsExactly32128Bytes() {
    var file = _CreateMinimalFile();
    var bytes = NeochromeWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(32128));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderPresent_ResolutionIsZero() {
    var file = _CreateMinimalFile();
    var bytes = NeochromeWriter.ToBytes(file);

    var resolution = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(2));
    Assert.That(resolution, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataAtOffset128() {
    var pixelData = new byte[32000];
    pixelData[0] = 0xAA;
    pixelData[1] = 0xBB;
    pixelData[31999] = 0xCC;

    var file = new NeochromeFile {
      Palette = new short[16],
      PixelData = pixelData
    };

    var bytes = NeochromeWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(bytes[128], Is.EqualTo(0xAA));
      Assert.That(bytes[129], Is.EqualTo(0xBB));
      Assert.That(bytes[32127], Is.EqualTo(0xCC));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesPaletteCorrectly() {
    var palette = new short[16];
    palette[0] = 0x0777; // white
    palette[1] = 0x0700; // red
    palette[15] = 0x0007; // blue

    var file = new NeochromeFile {
      Palette = palette,
      PixelData = new byte[32000]
    };

    var bytes = NeochromeWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(4)), Is.EqualTo((short)0x0777));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(6)), Is.EqualTo((short)0x0700));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(34)), Is.EqualTo((short)0x0007));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesAnimationFields() {
    var file = new NeochromeFile {
      Palette = new short[16],
      PixelData = new byte[32000],
      AnimSpeed = 3,
      AnimDirection = 1,
      AnimSteps = 8,
      AnimXOffset = 16,
      AnimYOffset = 32,
      AnimWidth = 64,
      AnimHeight = 48
    };

    var bytes = NeochromeWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(bytes[36], Is.EqualTo(3));
      Assert.That(bytes[37], Is.EqualTo(1));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(38)), Is.EqualTo(8));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(40)), Is.EqualTo(16));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(42)), Is.EqualTo(32));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(44)), Is.EqualTo(64));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(46)), Is.EqualTo(48));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ReservedAreaIsZero() {
    var file = new NeochromeFile {
      Palette = new short[16],
      PixelData = new byte[32000],
      Flag = 0x1234,
      AnimSpeed = 0xFF
    };

    var bytes = NeochromeWriter.ToBytes(file);

    for (var i = 48; i < 128; ++i)
      Assert.That(bytes[i], Is.EqualTo(0), $"Reserved byte at offset {i} should be zero");
  }

  private static NeochromeFile _CreateMinimalFile() => new() {
    Palette = new short[16],
    PixelData = new byte[32000]
  };
}
