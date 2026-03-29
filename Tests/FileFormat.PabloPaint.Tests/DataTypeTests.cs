using System;
using FileFormat.PabloPaint;
using FileFormat.Core;

namespace FileFormat.PabloPaint.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void FixedWidth_Is640()
    => Assert.That(new PabloPaintFile().Width, Is.EqualTo(640));

  [Test]
  [Category("Unit")]
  public void FixedHeight_Is400()
    => Assert.That(new PabloPaintFile().Height, Is.EqualTo(400));

  [Test]
  [Category("Unit")]
  public void DefaultPixelData_IsEmpty()
    => Assert.That(new PabloPaintFile().PixelData, Is.Empty);

  [Test]
  [Category("Unit")]
  public void FileSize_Is32000()
    => Assert.That(PabloPaintFile.FileSize, Is.EqualTo(32000));

  [Test]
  [Category("Unit")]
  public void InitPixelData_StoresCorrectly() {
    var pixelData = new byte[] { 0xAA, 0xBB };
    var file = new PabloPaintFile { PixelData = pixelData };
    Assert.That(file.PixelData, Is.SameAs(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PabloPaintFile.ToRawImage(null!));

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PabloPaintFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 640, Height = 400, Format = PixelFormat.Rgb24, PixelData = new byte[640 * 400 * 3] };
    Assert.Throws<ArgumentException>(() => PabloPaintFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongDimensions_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 320, Height = 200, Format = PixelFormat.Indexed1,
      PixelData = new byte[320 / 8 * 200], Palette = [0, 0, 0, 255, 255, 255], PaletteCount = 2,
    };
    Assert.Throws<ArgumentException>(() => PabloPaintFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesIndexed1() {
    var file = new PabloPaintFile { PixelData = new byte[32000] };
    var raw = PabloPaintFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_HasCorrectDimensions() {
    var file = new PabloPaintFile { PixelData = new byte[32000] };
    var raw = PabloPaintFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(640));
      Assert.That(raw.Height, Is.EqualTo(400));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_PaletteIsWhiteThenBlack() {
    var file = new PabloPaintFile { PixelData = new byte[32000] };
    var raw = PabloPaintFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(raw.Palette![0], Is.EqualTo(255));
      Assert.That(raw.Palette![1], Is.EqualTo(255));
      Assert.That(raw.Palette![2], Is.EqualTo(255));
      Assert.That(raw.Palette![3], Is.EqualTo(0));
      Assert.That(raw.Palette![4], Is.EqualTo(0));
      Assert.That(raw.Palette![5], Is.EqualTo(0));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_PixelDataSize() {
    var file = new PabloPaintFile { PixelData = new byte[32000] };
    var raw = PabloPaintFile.ToRawImage(file);
    Assert.That(raw.PixelData.Length, Is.EqualTo(640 / 8 * 400));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var file = new PabloPaintFile { PixelData = new byte[32000] };
    var raw1 = PabloPaintFile.ToRawImage(file);
    var raw2 = PabloPaintFile.ToRawImage(file);
    Assert.That(raw1.PixelData, Is.Not.SameAs(raw2.PixelData));
    Assert.That(raw1.PixelData, Is.EqualTo(raw2.PixelData));
  }
}
