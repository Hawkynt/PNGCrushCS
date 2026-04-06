using FileFormat.NokiaPictureMessage;
using FileFormat.Core;

namespace FileFormat.NokiaPictureMessage.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void NokiaPictureMessageFile_DefaultPixelData_IsEmptyArray() {
    var file = new NokiaPictureMessageFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData.Length, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void NokiaPictureMessageFile_InitProperties_RoundTrip() {
    var pixelData = new byte[] { 0xFF, 0x00 };
    var file = new NokiaPictureMessageFile {
      Width = 8,
      Height = 2,
      PixelData = pixelData
    };

    Assert.That(file.Width, Is.EqualTo(8));
    Assert.That(file.Height, Is.EqualTo(2));
    Assert.That(file.PixelData, Is.SameAs(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_PaletteIsWhiteBlack_NokiaConvention() {
    var file = new NokiaPictureMessageFile {
      Width = 8,
      Height = 1,
      PixelData = new byte[] { 0xFF }
    };

    var raw = NokiaPictureMessageFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
    Assert.That(raw.PaletteCount, Is.EqualTo(2));
    // Nokia convention: index 0 = white, index 1 = black
    Assert.That(raw.Palette![0], Is.EqualTo(255)); // White R
    Assert.That(raw.Palette![1], Is.EqualTo(255)); // White G
    Assert.That(raw.Palette![2], Is.EqualTo(255)); // White B
    Assert.That(raw.Palette![3], Is.EqualTo(0));   // Black R
    Assert.That(raw.Palette![4], Is.EqualTo(0));   // Black G
    Assert.That(raw.Palette![5], Is.EqualTo(0));   // Black B
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_Throws() {
    var raw = new RawImage {
      Width = 8,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[24],
    };

    Assert.Throws<System.ArgumentException>(() => NokiaPictureMessageFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WidthTooLarge_Throws() {
    var raw = new RawImage {
      Width = 256,
      Height = 1,
      Format = PixelFormat.Indexed1,
      PixelData = new byte[32],
      Palette = new byte[6],
      PaletteCount = 2,
    };

    Assert.Throws<System.ArgumentOutOfRangeException>(() => NokiaPictureMessageFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_HeightTooLarge_Throws() {
    var raw = new RawImage {
      Width = 8,
      Height = 256,
      Format = PixelFormat.Indexed1,
      PixelData = new byte[256],
      Palette = new byte[6],
      PaletteCount = 2,
    };

    Assert.Throws<System.ArgumentOutOfRangeException>(() => NokiaPictureMessageFile.FromRawImage(raw));
  }
}
