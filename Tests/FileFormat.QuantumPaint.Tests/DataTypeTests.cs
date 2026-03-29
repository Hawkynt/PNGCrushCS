using System;
using FileFormat.QuantumPaint;
using FileFormat.Core;

namespace FileFormat.QuantumPaint.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void FixedWidth_Is320()
    => Assert.That(new QuantumPaintFile().Width, Is.EqualTo(320));

  [Test]
  [Category("Unit")]
  public void FixedHeight_Is200()
    => Assert.That(new QuantumPaintFile().Height, Is.EqualTo(200));

  [Test]
  [Category("Unit")]
  public void DefaultPalette_Has16Entries()
    => Assert.That(new QuantumPaintFile().Palette.Length, Is.EqualTo(16));

  [Test]
  [Category("Unit")]
  public void DefaultPixelData_IsEmpty()
    => Assert.That(new QuantumPaintFile().PixelData, Is.Empty);

  [Test]
  [Category("Unit")]
  public void MinFileSize_Is32032()
    => Assert.That(QuantumPaintFile.MinFileSize, Is.EqualTo(32032));

  [Test]
  [Category("Unit")]
  public void InitPalette_StoresCorrectly() {
    var palette = new short[16];
    palette[0] = 0x0777;
    var file = new QuantumPaintFile { Palette = palette };
    Assert.That(file.Palette, Is.SameAs(palette));
  }

  [Test]
  [Category("Unit")]
  public void InitPixelData_StoresCorrectly() {
    var pixelData = new byte[] { 0xAA, 0xBB };
    var file = new QuantumPaintFile { PixelData = pixelData };
    Assert.That(file.PixelData, Is.SameAs(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => QuantumPaintFile.ToRawImage(null!));

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => QuantumPaintFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 320, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[320 * 200 * 3] };
    Assert.Throws<ArgumentException>(() => QuantumPaintFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongWidth_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 640, Height = 200, Format = PixelFormat.Indexed8,
      PixelData = new byte[640 * 200], Palette = new byte[48], PaletteCount = 16,
    };
    Assert.Throws<ArgumentException>(() => QuantumPaintFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongHeight_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 320, Height = 400, Format = PixelFormat.Indexed8,
      PixelData = new byte[320 * 400], Palette = new byte[48], PaletteCount = 16,
    };
    Assert.Throws<ArgumentException>(() => QuantumPaintFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesIndexed8() {
    var file = new QuantumPaintFile { Palette = new short[16], PixelData = new byte[32000] };
    var raw = QuantumPaintFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_HasCorrectDimensions() {
    var file = new QuantumPaintFile { Palette = new short[16], PixelData = new byte[32000] };
    var raw = QuantumPaintFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(320));
      Assert.That(raw.Height, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Has16PaletteEntries() {
    var file = new QuantumPaintFile { Palette = new short[16], PixelData = new byte[32000] };
    var raw = QuantumPaintFile.ToRawImage(file);
    Assert.That(raw.PaletteCount, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_PixelDataSize() {
    var file = new QuantumPaintFile { Palette = new short[16], PixelData = new byte[32000] };
    var raw = QuantumPaintFile.ToRawImage(file);
    Assert.That(raw.PixelData.Length, Is.EqualTo(320 * 200));
  }
}
