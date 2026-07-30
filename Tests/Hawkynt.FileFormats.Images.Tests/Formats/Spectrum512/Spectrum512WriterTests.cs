using System;
using System.Buffers.Binary;
using FileFormat.Spectrum512;

namespace FileFormat.Spectrum512.Tests;

[TestFixture]
public sealed class Spectrum512WriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_ExactSize51104() {
    var file = _BuildFile();
    var bytes = Spectrum512Writer.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(51104));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var file = _BuildFile();
    for (var i = 0; i < 32000; ++i)
      file.PixelData[i] = (byte)(i * 3 & 0xFF);

    var bytes = Spectrum512Writer.ToBytes(file);

    for (var i = 0; i < 32000; ++i)
      Assert.That(bytes[i], Is.EqualTo((byte)(i * 3 & 0xFF)), $"Mismatch at pixel byte {i}");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PaletteDataPreserved() {
    var file = _BuildFile();
    file.Palettes[0][0] = 0x777;
    file.Palettes[0][1] = 0x700;
    file.Palettes[198][47] = 0x007;

    var bytes = Spectrum512Writer.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(32000)), Is.EqualTo(0x777));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(32002)), Is.EqualTo(0x700));
      var lastOffset = 32000 + (198 * 48 + 47) * 2;
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(lastOffset)), Is.EqualTo(0x007));
    });
  }

  private static Spectrum512File _BuildFile() {
    var palettes = new short[199][];
    for (var i = 0; i < 199; ++i)
      palettes[i] = new short[48];

    return new Spectrum512File {
      Width = 320,
      Height = 199,
      Variant = Spectrum512Variant.Uncompressed,
      PixelData = new byte[32000],
      Palettes = palettes
    };
  }
}
