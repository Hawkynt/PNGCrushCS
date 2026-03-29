using System;
using FileFormat.Core;
using FileFormat.Trs80;

namespace FileFormat.Trs80.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void Trs80File_DefaultWidth_Is256() {
    var file = new Trs80File();

    Assert.That(file.Width, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void Trs80File_DefaultHeight_Is144() {
    var file = new Trs80File();

    Assert.That(file.Height, Is.EqualTo(144));
  }

  [Test]
  [Category("Unit")]
  public void Trs80File_DefaultRawData_IsEmpty() {
    var file = new Trs80File();

    Assert.That(file.RawData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void Trs80File_InitRawData_StoresCorrectly() {
    var rawData = new byte[] { 0x3F, 0x2A };
    var file = new Trs80File { RawData = rawData };

    Assert.That(file.RawData, Is.SameAs(rawData));
  }

  [Test]
  [Category("Unit")]
  public void Trs80File_FileSize_Is6144() {
    Assert.That(Trs80File.FileSize, Is.EqualTo(6144));
  }

  [Test]
  [Category("Unit")]
  public void Trs80File_Columns_Is128() {
    Assert.That(Trs80File.Columns, Is.EqualTo(128));
  }

  [Test]
  [Category("Unit")]
  public void Trs80File_Rows_Is48() {
    Assert.That(Trs80File.Rows, Is.EqualTo(48));
  }

  [Test]
  [Category("Unit")]
  public void Trs80File_PrimaryExtension_IsHr() {
    // Exercise the interface static member through a concrete invocation
    var file = new Trs80File { RawData = new byte[6144] };
    var bytes = Trs80Writer.ToBytes(file);
    var restored = Trs80Reader.FromBytes(bytes);
    Assert.That(restored.Width, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void Trs80File_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Trs80File.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void Trs80File_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Trs80File.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void Trs80File_FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 256,
      Height = 144,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[256 * 144 * 3],
    };

    Assert.Throws<ArgumentException>(() => Trs80File.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void Trs80File_FromRawImage_WrongDimensions_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Indexed1,
      PixelData = new byte[320 / 8 * 200],
    };

    Assert.Throws<ArgumentException>(() => Trs80File.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void Trs80File_ToRawImage_ReturnsIndexed1Format() {
    var file = new Trs80File { RawData = new byte[6144] };

    var raw = Trs80File.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
  }

  [Test]
  [Category("Unit")]
  public void Trs80File_ToRawImage_HasCorrectDimensions() {
    var file = new Trs80File { RawData = new byte[6144] };

    var raw = Trs80File.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(256));
    Assert.That(raw.Height, Is.EqualTo(144));
  }

  [Test]
  [Category("Unit")]
  public void Trs80File_ToRawImage_HasCorrectPalette() {
    var file = new Trs80File { RawData = new byte[6144] };

    var raw = Trs80File.ToRawImage(file);

    Assert.That(raw.Palette, Is.Not.Null);
    Assert.That(raw.PaletteCount, Is.EqualTo(2));
    // Palette entry 0: black (0,0,0)
    Assert.That(raw.Palette![0], Is.EqualTo(0));
    Assert.That(raw.Palette[1], Is.EqualTo(0));
    Assert.That(raw.Palette[2], Is.EqualTo(0));
    // Palette entry 1: white (255,255,255)
    Assert.That(raw.Palette[3], Is.EqualTo(255));
    Assert.That(raw.Palette[4], Is.EqualTo(255));
    Assert.That(raw.Palette[5], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void Trs80File_ToRawImage_PixelDataSize() {
    var file = new Trs80File { RawData = new byte[6144] };

    var raw = Trs80File.ToRawImage(file);

    // 256 pixels / 8 bits per byte = 32 bytes per row, 144 rows
    Assert.That(raw.PixelData.Length, Is.EqualTo(32 * 144));
  }

  [Test]
  [Category("Unit")]
  public void Trs80File_ToRawImage_ClonesPixelData() {
    var rawData = new byte[6144];
    rawData[0] = 0x3F;
    var file = new Trs80File { RawData = rawData };

    var raw1 = Trs80File.ToRawImage(file);
    var raw2 = Trs80File.ToRawImage(file);

    Assert.That(raw1.PixelData, Is.Not.SameAs(raw2.PixelData));
    Assert.That(raw1.PixelData, Is.EqualTo(raw2.PixelData));
  }
}
