using System;
using FileFormat.Hireslace;
using FileFormat.Core;

namespace FileFormat.Hireslace.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void HireslaceFile_DefaultBitmap1_IsEmpty() {
    var file = new HireslaceFile();
    Assert.That(file.Bitmap1, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void HireslaceFile_DefaultScreen1_IsEmpty() {
    var file = new HireslaceFile();
    Assert.That(file.Screen1, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void HireslaceFile_DefaultBitmap2_IsEmpty() {
    var file = new HireslaceFile();
    Assert.That(file.Bitmap2, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void HireslaceFile_DefaultScreen2_IsEmpty() {
    var file = new HireslaceFile();
    Assert.That(file.Screen2, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void HireslaceFile_FixedWidth_Is320() {
    var file = new HireslaceFile();
    Assert.That(file.Width, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void HireslaceFile_FixedHeight_Is200() {
    var file = new HireslaceFile();
    Assert.That(file.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void HireslaceFile_DefaultLoadAddress_IsZero() {
    var file = new HireslaceFile();
    Assert.That(file.LoadAddress, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void BitmapDataSize_Is8000() {
    Assert.That(HireslaceFile.BitmapDataSize, Is.EqualTo(8000));
  }

  [Test]
  [Category("Unit")]
  public void ScreenDataSize_Is1000() {
    Assert.That(HireslaceFile.ScreenDataSize, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void LoadAddressSize_Is2() {
    Assert.That(HireslaceFile.LoadAddressSize, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ExpectedFileSize_Is18002() {
    Assert.That(HireslaceFile.ExpectedFileSize, Is.EqualTo(18002));
  }

  [Test]
  [Category("Unit")]
  public void C64Palette_Has16Entries() {
    Assert.That(HireslaceFile.C64Palette.Length, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void C64Palette_BlackIsFirst() {
    Assert.That(HireslaceFile.C64Palette[0], Is.EqualTo(0x000000));
  }

  [Test]
  [Category("Unit")]
  public void C64Palette_WhiteIsSecond() {
    Assert.That(HireslaceFile.C64Palette[1], Is.EqualTo(0xFFFFFF));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HireslaceFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HireslaceFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void HireslaceFile_InitProperties() {
    var file = new HireslaceFile {
      LoadAddress = 0x2000,
      Bitmap1 = new byte[HireslaceFile.BitmapDataSize],
      Screen1 = new byte[HireslaceFile.ScreenDataSize],
      Bitmap2 = new byte[HireslaceFile.BitmapDataSize],
      Screen2 = new byte[HireslaceFile.ScreenDataSize],
    };

    Assert.That(file.LoadAddress, Is.EqualTo(0x2000));
    Assert.That(file.Bitmap1.Length, Is.EqualTo(8000));
    Assert.That(file.Screen1.Length, Is.EqualTo(1000));
    Assert.That(file.Bitmap2.Length, Is.EqualTo(8000));
    Assert.That(file.Screen2.Length, Is.EqualTo(1000));
  }
}
