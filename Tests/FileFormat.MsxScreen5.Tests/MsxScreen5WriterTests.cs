using System;
using FileFormat.MsxScreen5;

namespace FileFormat.MsxScreen5.Tests;

[TestFixture]
public sealed class MsxScreen5WriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxScreen5Writer.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WithoutPalette_SizeMatchesPixelData() {
    var file = new MsxScreen5File {
      PixelData = new byte[MsxScreen5File.PixelDataSize],
      Palette = null,
      HasBsaveHeader = false
    };

    var bytes = MsxScreen5Writer.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(MsxScreen5File.PixelDataSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WithPalette_SizeIncludesPalette() {
    var file = new MsxScreen5File {
      PixelData = new byte[MsxScreen5File.PixelDataSize],
      Palette = new byte[MsxScreen5File.PaletteSize],
      HasBsaveHeader = false
    };

    var bytes = MsxScreen5Writer.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(MsxScreen5File.FullDataSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WithBsaveHeader_PrependsMagicByte() {
    var file = new MsxScreen5File {
      PixelData = new byte[MsxScreen5File.PixelDataSize],
      Palette = new byte[MsxScreen5File.PaletteSize],
      HasBsaveHeader = true
    };

    var bytes = MsxScreen5Writer.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(MsxScreen5File.BsaveMagic));
    Assert.That(bytes.Length, Is.EqualTo(MsxScreen5File.BsaveHeaderSize + MsxScreen5File.FullDataSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixelData = new byte[MsxScreen5File.PixelDataSize];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 3 % 256);

    var file = new MsxScreen5File {
      PixelData = pixelData,
      Palette = null,
      HasBsaveHeader = false
    };

    var bytes = MsxScreen5Writer.ToBytes(file);

    for (var i = 0; i < MsxScreen5File.PixelDataSize; ++i)
      Assert.That(bytes[i], Is.EqualTo(pixelData[i]));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PaletteDataPreserved() {
    var palette = new byte[MsxScreen5File.PaletteSize];
    for (var i = 0; i < palette.Length; ++i)
      palette[i] = (byte)(i * 7 % 256);

    var file = new MsxScreen5File {
      PixelData = new byte[MsxScreen5File.PixelDataSize],
      Palette = palette,
      HasBsaveHeader = false
    };

    var bytes = MsxScreen5Writer.ToBytes(file);

    for (var i = 0; i < MsxScreen5File.PaletteSize; ++i)
      Assert.That(bytes[MsxScreen5File.PixelDataSize + i], Is.EqualTo(palette[i]));
  }
}
