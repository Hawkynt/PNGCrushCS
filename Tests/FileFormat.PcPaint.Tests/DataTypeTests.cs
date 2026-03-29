using System;
using FileFormat.Core;
using FileFormat.PcPaint;

namespace FileFormat.PcPaint.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void PcPaintFile_DefaultWidth_IsZero() {
    var file = new PcPaintFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PcPaintFile_DefaultHeight_IsZero() {
    var file = new PcPaintFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PcPaintFile_DefaultPalette_IsEmpty() {
    var file = new PcPaintFile();
    Assert.That(file.Palette, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void PcPaintFile_DefaultPixelData_IsEmpty() {
    var file = new PcPaintFile();
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void PcPaintFile_DefaultPlanes_IsOne() {
    var file = new PcPaintFile();
    Assert.That(file.Planes, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void PcPaintFile_DefaultBitsPerPixel_IsEight() {
    var file = new PcPaintFile();
    Assert.That(file.BitsPerPixel, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void PcPaintFile_DefaultXOffset_IsZero() {
    var file = new PcPaintFile();
    Assert.That(file.XOffset, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PcPaintFile_DefaultYOffset_IsZero() {
    var file = new PcPaintFile();
    Assert.That(file.YOffset, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PcPaintFile_DefaultXAspect_IsZero() {
    var file = new PcPaintFile();
    Assert.That(file.XAspect, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PcPaintFile_DefaultYAspect_IsZero() {
    var file = new PcPaintFile();
    Assert.That(file.YAspect, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PcPaintFile_Magic_Is0x1234() {
    Assert.That(PcPaintFile.Magic, Is.EqualTo(0x1234));
  }

  [Test]
  [Category("Unit")]
  public void PcPaintFile_HeaderSize_Is18() {
    Assert.That(PcPaintFile.HeaderSize, Is.EqualTo(18));
  }

  [Test]
  [Category("Unit")]
  public void PcPaintFile_PaletteSize_Is768() {
    Assert.That(PcPaintFile.PaletteSize, Is.EqualTo(768));
  }

  [Test]
  [Category("Unit")]
  public void PcPaintFile_PrimaryExtension_IsPic() {
    var ext = _GetPrimaryExtension<PcPaintFile>();
    Assert.That(ext, Is.EqualTo(".pic"));
  }

  [Test]
  [Category("Unit")]
  public void PcPaintFile_FileExtensions_ContainsPicAndClp() {
    var extensions = _GetFileExtensions<PcPaintFile>();
    Assert.That(extensions, Has.Length.EqualTo(2));
    Assert.That(extensions, Does.Contain(".pic"));
    Assert.That(extensions, Does.Contain(".clp"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PcPaintFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PcPaintFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[12],
    };
    Assert.Throws<ArgumentException>(() => PcPaintFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_NoPalette_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[4],
      Palette = null,
    };
    Assert.Throws<ArgumentException>(() => PcPaintFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsIndexed8() {
    var file = new PcPaintFile {
      Width = 2,
      Height = 2,
      Planes = 1,
      BitsPerPixel = 8,
      Palette = new byte[PcPaintFile.PaletteSize],
      PixelData = new byte[4],
    };

    var raw = PcPaintFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixelData = new byte[] { 1, 2, 3, 4 };
    var file = new PcPaintFile {
      Width = 2,
      Height = 2,
      Planes = 1,
      BitsPerPixel = 8,
      Palette = new byte[PcPaintFile.PaletteSize],
      PixelData = pixelData,
    };

    var raw = PcPaintFile.ToRawImage(file);
    pixelData[0] = 99;

    Assert.That(raw.PixelData[0], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPalette() {
    var palette = new byte[PcPaintFile.PaletteSize];
    palette[0] = 42;
    var file = new PcPaintFile {
      Width = 2,
      Height = 2,
      Planes = 1,
      BitsPerPixel = 8,
      Palette = palette,
      PixelData = new byte[4],
    };

    var raw = PcPaintFile.ToRawImage(file);
    palette[0] = 99;

    Assert.That(raw.Palette![0], Is.EqualTo(42));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPixelData() {
    var pixelData = new byte[] { 1, 2, 3, 4 };
    var raw = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Indexed8,
      PixelData = pixelData,
      Palette = new byte[PcPaintFile.PaletteSize],
      PaletteCount = 256,
    };

    var file = PcPaintFile.FromRawImage(raw);
    pixelData[0] = 99;

    Assert.That(file.PixelData[0], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void PcPaintFile_InitProperties_RoundTrip() {
    var file = new PcPaintFile {
      Width = 100,
      Height = 50,
      XOffset = 10,
      YOffset = 20,
      Planes = 1,
      BitsPerPixel = 8,
      XAspect = 3,
      YAspect = 4,
    };

    Assert.That(file.Width, Is.EqualTo(100));
    Assert.That(file.Height, Is.EqualTo(50));
    Assert.That(file.XOffset, Is.EqualTo(10));
    Assert.That(file.YOffset, Is.EqualTo(20));
    Assert.That(file.Planes, Is.EqualTo(1));
    Assert.That(file.BitsPerPixel, Is.EqualTo(8));
    Assert.That(file.XAspect, Is.EqualTo(3));
    Assert.That(file.YAspect, Is.EqualTo(4));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T>
    => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T>
    => T.FileExtensions;
}
