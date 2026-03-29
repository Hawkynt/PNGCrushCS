using System;
using FileFormat.Nie;

namespace FileFormat.Nie.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void NiePixelConfig_HasExpectedValues() {
    Assert.Multiple(() => {
      Assert.That((byte)NiePixelConfig.Bgra8, Is.EqualTo(0x62));
      Assert.That((byte)NiePixelConfig.BgraPremul8, Is.EqualTo(0x70));
      Assert.That((byte)NiePixelConfig.Bgra16, Is.EqualTo(0x42));
      Assert.That((byte)NiePixelConfig.BgraPremul16, Is.EqualTo(0x50));
    });
    Assert.That(Enum.GetValues<NiePixelConfig>(), Has.Length.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void NiePixelConfig_MatchesAsciiChars() {
    Assert.Multiple(() => {
      Assert.That((char)(byte)NiePixelConfig.Bgra8, Is.EqualTo('b'));
      Assert.That((char)(byte)NiePixelConfig.BgraPremul8, Is.EqualTo('p'));
      Assert.That((char)(byte)NiePixelConfig.Bgra16, Is.EqualTo('B'));
      Assert.That((char)(byte)NiePixelConfig.BgraPremul16, Is.EqualTo('P'));
    });
  }

  [Test]
  [Category("Unit")]
  public void HeaderSize_Is16()
    => Assert.That(NieFile.HeaderSize, Is.EqualTo(16));

  [Test]
  [Category("Unit")]
  public void MagicBytes_Correct() {
    Assert.Multiple(() => {
      Assert.That(NieFile.MagicBytes[0], Is.EqualTo(0x6E));
      Assert.That(NieFile.MagicBytes[1], Is.EqualTo(0xC3));
      Assert.That(NieFile.MagicBytes[2], Is.EqualTo(0xAF));
      Assert.That(NieFile.MagicBytes[3], Is.EqualTo(0x45));
    });
  }

  [Test]
  [Category("Unit")]
  public void NieFile_Defaults() {
    var file = new NieFile();
    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(0));
      Assert.That(file.Height, Is.EqualTo(0));
      Assert.That(file.PixelConfig, Is.EqualTo((NiePixelConfig)0));
      Assert.That(file.PixelData, Is.Empty);
      Assert.That(file.IsPremultiplied, Is.False);
      Assert.That(file.Is16Bit, Is.False);
      Assert.That(file.BytesPerPixel, Is.EqualTo(4));
    });
  }

  [Test]
  [Category("Unit")]
  public void NieFile_BytesPerPixel_8bit()
    => Assert.That(new NieFile { PixelConfig = NiePixelConfig.Bgra8 }.BytesPerPixel, Is.EqualTo(4));

  [Test]
  [Category("Unit")]
  public void NieFile_BytesPerPixel_16bit()
    => Assert.That(new NieFile { PixelConfig = NiePixelConfig.Bgra16 }.BytesPerPixel, Is.EqualTo(8));

  [Test]
  [Category("Unit")]
  public void NieFile_FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => NieFile.FromRawImage(null!));
}
