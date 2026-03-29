using System;
using FileFormat.Core;
using FileFormat.InterlaceStudio;

namespace FileFormat.InterlaceStudio.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void InterlaceStudioFile_ImageWidth_Is160() {
    Assert.That(InterlaceStudioFile.ImageWidth, Is.EqualTo(160));
  }

  [Test]
  [Category("Unit")]
  public void InterlaceStudioFile_ImageHeight_Is200() {
    Assert.That(InterlaceStudioFile.ImageHeight, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void InterlaceStudioFile_FileSize_Is19003() {
    Assert.That(InterlaceStudioFile.FileSize, Is.EqualTo(19003));
  }

  [Test]
  [Category("Unit")]
  public void InterlaceStudioFile_BitmapDataSize_Is8000() {
    Assert.That(InterlaceStudioFile.BitmapDataSize, Is.EqualTo(8000));
  }

  [Test]
  [Category("Unit")]
  public void InterlaceStudioFile_ScreenDataSize_Is1000() {
    Assert.That(InterlaceStudioFile.ScreenDataSize, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void InterlaceStudioFile_ColorDataSize_Is1000() {
    Assert.That(InterlaceStudioFile.ColorDataSize, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void InterlaceStudioFile_DefaultBitmap1_IsEmpty() {
    var file = new InterlaceStudioFile();
    Assert.That(file.Bitmap1, Is.Not.Null);
    Assert.That(file.Bitmap1, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void InterlaceStudioFile_DefaultScreen1_IsEmpty() {
    var file = new InterlaceStudioFile();
    Assert.That(file.Screen1, Is.Not.Null);
    Assert.That(file.Screen1, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void InterlaceStudioFile_DefaultColorData_IsEmpty() {
    var file = new InterlaceStudioFile();
    Assert.That(file.ColorData, Is.Not.Null);
    Assert.That(file.ColorData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void InterlaceStudioFile_DefaultBitmap2_IsEmpty() {
    var file = new InterlaceStudioFile();
    Assert.That(file.Bitmap2, Is.Not.Null);
    Assert.That(file.Bitmap2, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void InterlaceStudioFile_DefaultScreen2_IsEmpty() {
    var file = new InterlaceStudioFile();
    Assert.That(file.Screen2, Is.Not.Null);
    Assert.That(file.Screen2, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void InterlaceStudioFile_DefaultLoadAddress_IsZero() {
    var file = new InterlaceStudioFile();
    Assert.That(file.LoadAddress, Is.EqualTo((ushort)0));
  }

  [Test]
  [Category("Unit")]
  public void InterlaceStudioFile_DefaultBackgroundColor_IsZero() {
    var file = new InterlaceStudioFile();
    Assert.That(file.BackgroundColor, Is.EqualTo((byte)0));
  }

  [Test]
  [Category("Unit")]
  public void InterlaceStudioFile_InitProperties_StoreCorrectly() {
    var bitmap1 = new byte[] { 1, 2, 3 };
    var screen1 = new byte[] { 4, 5, 6 };
    var colorData = new byte[] { 7, 8, 9 };
    var bitmap2 = new byte[] { 10, 11, 12 };
    var screen2 = new byte[] { 13, 14, 15 };

    var file = new InterlaceStudioFile {
      LoadAddress = 0x1234,
      Bitmap1 = bitmap1,
      Screen1 = screen1,
      ColorData = colorData,
      Bitmap2 = bitmap2,
      Screen2 = screen2,
      BackgroundColor = 5,
    };

    Assert.That(file.LoadAddress, Is.EqualTo((ushort)0x1234));
    Assert.That(file.Bitmap1, Is.SameAs(bitmap1));
    Assert.That(file.Screen1, Is.SameAs(screen1));
    Assert.That(file.ColorData, Is.SameAs(colorData));
    Assert.That(file.Bitmap2, Is.SameAs(bitmap2));
    Assert.That(file.Screen2, Is.SameAs(screen2));
    Assert.That(file.BackgroundColor, Is.EqualTo((byte)5));
  }

  [Test]
  [Category("Unit")]
  public void InterlaceStudioFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => InterlaceStudioFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void InterlaceStudioFile_FromRawImage_ThrowsNotSupportedException() {
    var raw = new RawImage {
      Width = 160,
      Height = 200,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[160 * 200 * 3],
    };
    Assert.Throws<NotSupportedException>(() => InterlaceStudioFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void InterlaceStudioFile_PrimaryExtension() {
    var ext = _GetStaticProperty<string>("PrimaryExtension");
    Assert.That(ext, Is.EqualTo(".ist"));
  }

  [Test]
  [Category("Unit")]
  public void InterlaceStudioFile_FileExtensions() {
    var exts = _GetStaticProperty<string[]>("FileExtensions");
    Assert.That(exts, Has.Length.EqualTo(1));
    Assert.That(exts, Does.Contain(".ist"));
  }

  private static T _GetStaticProperty<T>(string name) {
    var map = typeof(InterlaceStudioFile).GetInterfaceMap(typeof(IImageFileFormat<InterlaceStudioFile>));
    foreach (var method in map.TargetMethods)
      if (method.Name.Contains(name))
        return (T)method.Invoke(null, null)!;
    throw new InvalidOperationException($"Property {name} not found.");
  }
}
