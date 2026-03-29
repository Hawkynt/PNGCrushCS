using System;
using FileFormat.MagicPainter;
using FileFormat.Core;

namespace FileFormat.MagicPainter.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void MagicPainterFile_DefaultPixelData_IsEmpty() {
    var file = new MagicPainterFile();
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void MagicPainterFile_DefaultPalette_IsEmpty() {
    var file = new MagicPainterFile();
    Assert.That(file.Palette, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void MagicPainterFile_DefaultPaletteCount_IsZero() {
    var file = new MagicPainterFile();
    Assert.That(file.PaletteCount, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void MagicPainterFile_DefaultWidth_IsZero() {
    var file = new MagicPainterFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void MagicPainterFile_DefaultHeight_IsZero() {
    var file = new MagicPainterFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void MagicPainterFile_InitProperties() {
    var file = new MagicPainterFile {
      Width = 100,
      Height = 50,
      PaletteCount = 16,
      Palette = new byte[48],
      PixelData = new byte[5000],
    };

    Assert.That(file.Width, Is.EqualTo(100));
    Assert.That(file.Height, Is.EqualTo(50));
    Assert.That(file.PaletteCount, Is.EqualTo(16));
    Assert.That(file.Palette.Length, Is.EqualTo(48));
    Assert.That(file.PixelData.Length, Is.EqualTo(5000));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MagicPainterFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MagicPainterFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 8,
      Height = 8,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[192],
    };
    Assert.Throws<ArgumentException>(() => MagicPainterFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_NoPalette_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 8,
      Height = 8,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[64],
      PaletteCount = 0,
    };
    Assert.Throws<ArgumentException>(() => MagicPainterFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesIndexed8() {
    var file = new MagicPainterFile {
      Width = 4,
      Height = 4,
      PaletteCount = 2,
      Palette = new byte[] { 0, 0, 0, 255, 255, 255 },
      PixelData = new byte[16],
    };

    var raw = MagicPainterFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
    Assert.That(raw.Width, Is.EqualTo(4));
    Assert.That(raw.Height, Is.EqualTo(4));
    Assert.That(raw.PaletteCount, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void HeaderSize_Is6() {
    Assert.That(MagicPainterReader.HeaderSize, Is.EqualTo(6));
  }

  [Test]
  [Category("Unit")]
  public void MaxPaletteEntries_Is256() {
    Assert.That(MagicPainterReader.MaxPaletteEntries, Is.EqualTo(256));
  }
}
