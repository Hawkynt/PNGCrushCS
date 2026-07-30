using System;
using System.Buffers.Binary;
using FileFormat.Degas;

namespace FileFormat.Degas.Tests;

[TestFixture]
public sealed class DegasWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Uncompressed_StartsWithCorrectResolution() {
    var file = new DegasFile {
      Width = 320,
      Height = 200,
      Resolution = DegasResolution.Low,
      IsCompressed = false,
      Palette = new short[16],
      PixelData = new byte[32000]
    };

    var bytes = DegasWriter.ToBytes(file);

    var resolution = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan());
    Assert.That(resolution, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Uncompressed_Is32034Bytes() {
    var file = new DegasFile {
      Width = 320,
      Height = 200,
      Resolution = DegasResolution.Low,
      IsCompressed = false,
      Palette = new short[16],
      PixelData = new byte[32000]
    };

    var bytes = DegasWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(32034));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Compressed_SetsCompressionBit() {
    var file = new DegasFile {
      Width = 320,
      Height = 200,
      Resolution = DegasResolution.Low,
      IsCompressed = true,
      Palette = new short[16],
      PixelData = new byte[32000]
    };

    var bytes = DegasWriter.ToBytes(file);

    var resolution = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan());
    Assert.That(resolution & unchecked((short)0x8000), Is.Not.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PreservesPaletteValues() {
    var palette = new short[16];
    palette[0] = 0x777; // white in Atari ST
    palette[1] = 0x700; // red
    palette[2] = 0x070; // green
    palette[3] = 0x007; // blue

    var file = new DegasFile {
      Width = 640,
      Height = 400,
      Resolution = DegasResolution.High,
      IsCompressed = false,
      Palette = palette,
      PixelData = new byte[32000]
    };

    var bytes = DegasWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(2)), Is.EqualTo(0x777));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(4)), Is.EqualTo(0x700));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(6)), Is.EqualTo(0x070));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(8)), Is.EqualTo(0x007));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HighRes_HasCorrectResolutionValue() {
    var file = new DegasFile {
      Width = 640,
      Height = 400,
      Resolution = DegasResolution.High,
      IsCompressed = false,
      Palette = new short[16],
      PixelData = new byte[32000]
    };

    var bytes = DegasWriter.ToBytes(file);

    var resolution = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan());
    Assert.That(resolution, Is.EqualTo(2));
  }
}
