using System;
using FileFormat.Core;
using FileFormat.LogoSys;

namespace FileFormat.LogoSys.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void LogoSysFile_DefaultWidth_Is320() {
    var file = new LogoSysFile();

    Assert.That(file.Width, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void LogoSysFile_DefaultHeight_Is400() {
    var file = new LogoSysFile();

    Assert.That(file.Height, Is.EqualTo(400));
  }

  [Test]
  [Category("Unit")]
  public void LogoSysFile_DefaultPalette_IsEmpty() {
    var file = new LogoSysFile();

    Assert.That(file.Palette, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void LogoSysFile_DefaultPixelData_IsEmpty() {
    var file = new LogoSysFile();

    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void LogoSysFile_InitPalette_StoresCorrectly() {
    var palette = new byte[] { 0xFF, 0x00, 0x80 };
    var file = new LogoSysFile { Palette = palette };

    Assert.That(file.Palette, Is.SameAs(palette));
  }

  [Test]
  [Category("Unit")]
  public void LogoSysFile_InitPixelData_StoresCorrectly() {
    var pixelData = new byte[] { 42, 99, 200 };
    var file = new LogoSysFile { PixelData = pixelData };

    Assert.That(file.PixelData, Is.SameAs(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void LogoSysFile_FileSize_Is128768() {
    Assert.That(LogoSysFile.FileSize, Is.EqualTo(128768));
  }

  [Test]
  [Category("Unit")]
  public void LogoSysFile_PaletteSize_Is768() {
    Assert.That(LogoSysFile.PaletteSize, Is.EqualTo(768));
  }

  [Test]
  [Category("Unit")]
  public void LogoSysFile_PixelDataSize_Is128000() {
    Assert.That(LogoSysFile.PixelDataSize, Is.EqualTo(128000));
  }

  [Test]
  [Category("Unit")]
  public void LogoSysFile_PaletteEntries_Is256() {
    Assert.That(LogoSysFile.PaletteEntries, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void LogoSysFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => LogoSysFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void LogoSysFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => LogoSysFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void LogoSysFile_FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 320,
      Height = 400,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[320 * 400 * 3],
    };

    Assert.Throws<ArgumentException>(() => LogoSysFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void LogoSysFile_FromRawImage_WrongDimensions_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 640,
      Height = 480,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[640 * 480],
      Palette = new byte[768],
      PaletteCount = 256,
    };

    Assert.Throws<ArgumentException>(() => LogoSysFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void LogoSysFile_FromRawImage_NullPalette_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 320,
      Height = 400,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[128000],
    };

    Assert.Throws<ArgumentException>(() => LogoSysFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void LogoSysFile_FromRawImage_ShortPalette_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 320,
      Height = 400,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[128000],
      Palette = new byte[10],
      PaletteCount = 3,
    };

    Assert.Throws<ArgumentException>(() => LogoSysFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void LogoSysFile_ToRawImage_ReturnsIndexed8Format() {
    var file = new LogoSysFile {
      Palette = new byte[768],
      PixelData = new byte[128000],
    };

    var raw = LogoSysFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
  }

  [Test]
  [Category("Unit")]
  public void LogoSysFile_ToRawImage_HasCorrectDimensions() {
    var file = new LogoSysFile {
      Palette = new byte[768],
      PixelData = new byte[128000],
    };

    var raw = LogoSysFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(320));
    Assert.That(raw.Height, Is.EqualTo(400));
  }

  [Test]
  [Category("Unit")]
  public void LogoSysFile_ToRawImage_HasPaletteWith256Entries() {
    var file = new LogoSysFile {
      Palette = new byte[768],
      PixelData = new byte[128000],
    };

    var raw = LogoSysFile.ToRawImage(file);

    Assert.That(raw.Palette, Is.Not.Null);
    Assert.That(raw.PaletteCount, Is.EqualTo(256));
    Assert.That(raw.Palette!.Length, Is.EqualTo(768));
  }

  [Test]
  [Category("Unit")]
  public void LogoSysFile_ToRawImage_PixelDataSize() {
    var file = new LogoSysFile {
      Palette = new byte[768],
      PixelData = new byte[128000],
    };

    var raw = LogoSysFile.ToRawImage(file);

    Assert.That(raw.PixelData.Length, Is.EqualTo(128000));
  }

  [Test]
  [Category("Unit")]
  public void LogoSysFile_ToRawImage_ClonesPixelData() {
    var file = new LogoSysFile {
      Palette = new byte[768],
      PixelData = new byte[128000],
    };
    file.PixelData[0] = 42;

    var raw1 = LogoSysFile.ToRawImage(file);
    var raw2 = LogoSysFile.ToRawImage(file);

    Assert.That(raw1.PixelData, Is.Not.SameAs(raw2.PixelData));
    Assert.That(raw1.PixelData, Is.EqualTo(raw2.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void LogoSysFile_ToRawImage_ClonesPalette() {
    var file = new LogoSysFile {
      Palette = new byte[768],
      PixelData = new byte[128000],
    };
    file.Palette[0] = 0xAA;

    var raw1 = LogoSysFile.ToRawImage(file);
    var raw2 = LogoSysFile.ToRawImage(file);

    Assert.That(raw1.Palette, Is.Not.SameAs(raw2.Palette));
    Assert.That(raw1.Palette, Is.EqualTo(raw2.Palette));
  }
}
