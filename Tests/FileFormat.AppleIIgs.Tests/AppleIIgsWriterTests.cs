using System;
using FileFormat.AppleIIgs;

namespace FileFormat.AppleIIgs.Tests;

[TestFixture]
public sealed class AppleIIgsWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AppleIIgsWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputIsExactly32768Bytes() {
    var file = _BuildValidFile(AppleIIgsMode.Mode320);
    var bytes = AppleIIgsWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(32768));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsScbs() {
    var file = _BuildValidFile(AppleIIgsMode.Mode320);
    file.Scbs[0] = 0x05;
    file.Scbs[199] = 0x0A;

    var bytes = AppleIIgsWriter.ToBytes(file);

    Assert.That(bytes[32000], Is.EqualTo(0x05));
    Assert.That(bytes[32199], Is.EqualTo(0x0A));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataOffset_StartsAtByte0() {
    var file = _BuildValidFile(AppleIIgsMode.Mode320);
    file.PixelData[0] = 0xAA;
    file.PixelData[31999] = 0xBB;

    var bytes = AppleIIgsWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0xAA));
    Assert.That(bytes[31999], Is.EqualTo(0xBB));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PaletteValues_WrittenAsLittleEndian() {
    var file = _BuildValidFile(AppleIIgsMode.Mode320);
    file.Palettes[0] = 0x1234;

    var bytes = AppleIIgsWriter.ToBytes(file);

    Assert.That(bytes[32200], Is.EqualTo(0x34));
    Assert.That(bytes[32201], Is.EqualTo(0x12));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PaddingIsZero() {
    var file = _BuildValidFile(AppleIIgsMode.Mode320);
    var bytes = AppleIIgsWriter.ToBytes(file);

    for (var i = 32712; i < 32768; ++i)
      Assert.That(bytes[i], Is.EqualTo(0), $"Padding byte at offset {i} should be zero.");
  }

  private static AppleIIgsFile _BuildValidFile(AppleIIgsMode mode) {
    var pixelData = new byte[32000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var scbs = new byte[200];
    var scbValue = mode == AppleIIgsMode.Mode640 ? (byte)0x80 : (byte)0x00;
    for (var i = 0; i < scbs.Length; ++i)
      scbs[i] = scbValue;

    var palettes = new short[256];
    for (var i = 0; i < palettes.Length; ++i)
      palettes[i] = (short)(i * 17 % 4096);

    return new AppleIIgsFile {
      Width = mode == AppleIIgsMode.Mode640 ? 640 : 320,
      Height = 200,
      Mode = mode,
      PixelData = pixelData,
      Scbs = scbs,
      Palettes = palettes
    };
  }
}
