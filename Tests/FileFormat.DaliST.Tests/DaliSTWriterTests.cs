using System;
using System.Buffers.Binary;
using FileFormat.DaliST;

namespace FileFormat.DaliST.Tests;

[TestFixture]
public sealed class DaliSTWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DaliSTWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputIs32032Bytes() {
    var file = new DaliSTFile {
      Width = 320,
      Height = 200,
      Resolution = DaliSTResolution.Low,
      Palette = new short[16],
      PixelData = new byte[32000]
    };

    var bytes = DaliSTWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(32032));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PreservesPaletteValues() {
    var palette = new short[16];
    palette[0] = 0x777;
    palette[1] = 0x700;
    palette[2] = 0x070;
    palette[3] = 0x007;

    var file = new DaliSTFile {
      Width = 320,
      Height = 200,
      Resolution = DaliSTResolution.Low,
      Palette = palette,
      PixelData = new byte[32000]
    };

    var bytes = DaliSTWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(0)), Is.EqualTo(0x777));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(2)), Is.EqualTo(0x700));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(4)), Is.EqualTo(0x070));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(6)), Is.EqualTo(0x007));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataStartsAtOffset32() {
    var pixelData = new byte[32000];
    pixelData[0] = 0xAB;
    pixelData[1] = 0xCD;

    var file = new DaliSTFile {
      Width = 320,
      Height = 200,
      Resolution = DaliSTResolution.Low,
      Palette = new short[16],
      PixelData = pixelData
    };

    var bytes = DaliSTWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(bytes[32], Is.EqualTo(0xAB));
      Assert.That(bytes[33], Is.EqualTo(0xCD));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_AllZeroPalette_WritesZeros() {
    var file = new DaliSTFile {
      Width = 640,
      Height = 400,
      Resolution = DaliSTResolution.High,
      Palette = new short[16],
      PixelData = new byte[32000]
    };

    var bytes = DaliSTWriter.ToBytes(file);

    for (var i = 0; i < 32; ++i)
      Assert.That(bytes[i], Is.EqualTo(0), $"Byte at offset {i} should be zero");
  }
}
