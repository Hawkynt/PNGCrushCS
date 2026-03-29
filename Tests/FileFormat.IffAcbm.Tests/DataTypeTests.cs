using System;
using FileFormat.IffAcbm;
using FileFormat.Core;

namespace FileFormat.IffAcbm.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void IffAcbmFile_DefaultWidth_IsZero() {
    var file = new IffAcbmFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void IffAcbmFile_DefaultHeight_IsZero() {
    var file = new IffAcbmFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void IffAcbmFile_DefaultNumPlanes_IsZero() {
    var file = new IffAcbmFile();
    Assert.That(file.NumPlanes, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void IffAcbmFile_DefaultPixelData_IsEmpty() {
    var file = new IffAcbmFile();
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void IffAcbmFile_DefaultPalette_IsEmpty() {
    var file = new IffAcbmFile();
    Assert.That(file.Palette, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void IffAcbmFile_InitProperties_RoundTrip() {
    var file = new IffAcbmFile {
      Width = 100,
      Height = 50,
      NumPlanes = 4,
      XAspect = 10,
      YAspect = 11,
      PageWidth = 320,
      PageHeight = 200,
      TransparentColor = 3,
    };

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(100));
      Assert.That(file.Height, Is.EqualTo(50));
      Assert.That(file.NumPlanes, Is.EqualTo(4));
      Assert.That(file.XAspect, Is.EqualTo(10));
      Assert.That(file.YAspect, Is.EqualTo(11));
      Assert.That(file.PageWidth, Is.EqualTo(320));
      Assert.That(file.PageHeight, Is.EqualTo(200));
      Assert.That(file.TransparentColor, Is.EqualTo(3));
    });
  }

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsAcbm() {
    var ext = GetPrimaryExtension();
    Assert.That(ext, Is.EqualTo(".acbm"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsAcbmAndIff() {
    var extensions = GetFileExtensions();
    Assert.Multiple(() => {
      Assert.That(extensions, Has.Length.EqualTo(2));
      Assert.That(extensions, Does.Contain(".acbm"));
      Assert.That(extensions, Does.Contain(".iff"));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffAcbmFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffAcbmFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_UnsupportedFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[2 * 2 * 3],
    };
    Assert.Throws<ArgumentException>(() => IffAcbmFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsIndexed8() {
    var file = TestHelper.CreateTestFile(8, 2, 2);
    var raw = IffAcbmFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var file = TestHelper.CreateTestFile(8, 2, 2);
    var raw = IffAcbmFile.ToRawImage(file);

    Assert.That(raw.PixelData, Is.Not.SameAs(file.PixelData));
    Assert.That(raw.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPalette() {
    var file = TestHelper.CreateTestFile(8, 2, 2);
    var raw = IffAcbmFile.ToRawImage(file);

    Assert.That(raw.Palette, Is.Not.Null);
    Assert.That(raw.Palette, Is.Not.SameAs(file.Palette));
    Assert.That(raw.Palette, Is.EqualTo(file.Palette));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPixelData() {
    var raw = new RawImage {
      Width = 4,
      Height = 2,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[4 * 2],
      Palette = new byte[4 * 3],
      PaletteCount = 4,
    };

    var file = IffAcbmFile.FromRawImage(raw);
    Assert.That(file.PixelData, Is.Not.SameAs(raw.PixelData));
    Assert.That(file.PixelData, Is.EqualTo(raw.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_CalculatesNumPlanes() {
    var raw = new RawImage {
      Width = 4,
      Height = 2,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[4 * 2],
      Palette = new byte[16 * 3],
      PaletteCount = 16,
    };

    var file = IffAcbmFile.FromRawImage(raw);
    Assert.That(file.NumPlanes, Is.EqualTo(4)); // ceil(log2(16)) = 4
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_2Colors_1Plane() {
    var raw = new RawImage {
      Width = 4,
      Height = 2,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[4 * 2],
      Palette = new byte[2 * 3],
      PaletteCount = 2,
    };

    var file = IffAcbmFile.FromRawImage(raw);
    Assert.That(file.NumPlanes, Is.EqualTo(1));
  }

  private static string GetPrimaryExtension() => _CallStaticInterfaceProperty<string>("PrimaryExtension");
  private static string[] GetFileExtensions() => _CallStaticInterfaceProperty<string[]>("FileExtensions");

  private static T _CallStaticInterfaceProperty<T>(string name) {
    var interfaceMap = typeof(IffAcbmFile).GetInterfaceMap(typeof(IImageFileFormat<IffAcbmFile>));
    for (var i = 0; i < interfaceMap.InterfaceMethods.Length; ++i)
      if (interfaceMap.InterfaceMethods[i].Name.Contains(name))
        return (T)interfaceMap.TargetMethods[i].Invoke(null, null)!;
    throw new InvalidOperationException($"Could not find interface property '{name}'.");
  }
}
