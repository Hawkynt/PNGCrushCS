using System;
using System.Buffers.Binary;
using FileFormat.CrackArt;

namespace FileFormat.CrackArt.Tests;

[TestFixture]
public sealed class CrackArtWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CrackArtWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithCorrectResolution() {
    var file = new CrackArtFile {
      Width = 320,
      Height = 200,
      Resolution = CrackArtResolution.Low,
      Palette = new short[16],
      PixelData = new byte[32000]
    };

    var bytes = CrackArtWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HighRes_HasCorrectResolutionValue() {
    var file = new CrackArtFile {
      Width = 640,
      Height = 400,
      Resolution = CrackArtResolution.High,
      Palette = new short[16],
      PixelData = new byte[32000]
    };

    var bytes = CrackArtWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PreservesPaletteValues() {
    var palette = new short[16];
    palette[0] = 0x777;
    palette[1] = 0x700;
    palette[2] = 0x070;
    palette[3] = 0x007;

    var file = new CrackArtFile {
      Width = 640,
      Height = 400,
      Resolution = CrackArtResolution.High,
      Palette = palette,
      PixelData = new byte[32000]
    };

    var bytes = CrackArtWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(1)), Is.EqualTo(0x777));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(3)), Is.EqualTo(0x700));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(5)), Is.EqualTo(0x070));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(7)), Is.EqualTo(0x007));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputLargerThanHeaderOnly() {
    var file = new CrackArtFile {
      Width = 320,
      Height = 200,
      Resolution = CrackArtResolution.Low,
      Palette = new short[16],
      PixelData = new byte[32000]
    };

    var bytes = CrackArtWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThan(33));
  }
}
