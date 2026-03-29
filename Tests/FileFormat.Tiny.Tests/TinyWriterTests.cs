using System;
using System.Buffers.Binary;
using FileFormat.Tiny;

namespace FileFormat.Tiny.Tests;

[TestFixture]
public sealed class TinyWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_ResolutionByteCorrect() {
    var file = new TinyFile {
      Width = 640,
      Height = 200,
      Resolution = TinyResolution.Medium,
      Palette = new short[16],
      PixelData = new byte[32000]
    };

    var bytes = TinyWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)TinyResolution.Medium));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PaletteWritten() {
    var palette = new short[16];
    palette[0] = 0x777;
    palette[1] = 0x700;
    palette[15] = 0x007;

    var file = new TinyFile {
      Width = 320,
      Height = 200,
      Resolution = TinyResolution.Low,
      Palette = palette,
      PixelData = new byte[32000]
    };

    var bytes = TinyWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(1)), Is.EqualTo(0x777));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(3)), Is.EqualTo(0x700));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(31)), Is.EqualTo(0x007));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CompressedDataWritten() {
    var file = new TinyFile {
      Width = 320,
      Height = 200,
      Resolution = TinyResolution.Low,
      Palette = new short[16],
      PixelData = new byte[32000]
    };

    var bytes = TinyWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThan(33));
  }
}
