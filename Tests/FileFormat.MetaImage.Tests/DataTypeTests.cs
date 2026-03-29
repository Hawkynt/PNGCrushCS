using System;
using FileFormat.Core;
using FileFormat.MetaImage;

namespace FileFormat.MetaImage.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void MetaImageElementType_HasFourValues() {
    var values = Enum.GetValues<MetaImageElementType>();
    Assert.That(values, Has.Length.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void MetaImageElementType_MetUChar_IsZero() {
    Assert.That((int)MetaImageElementType.MetUChar, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void MetaImageElementType_MetShort_IsOne() {
    Assert.That((int)MetaImageElementType.MetShort, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void MetaImageElementType_MetUShort_IsTwo() {
    Assert.That((int)MetaImageElementType.MetUShort, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void MetaImageElementType_MetFloat_IsThree() {
    Assert.That((int)MetaImageElementType.MetFloat, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void MetaImageFile_DefaultPixelData_IsEmptyArray() {
    var file = new MetaImageFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void MetaImageFile_DefaultChannels_IsOne() {
    var file = new MetaImageFile();
    Assert.That(file.Channels, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void MetaImageFile_DefaultIsCompressed_IsFalse() {
    var file = new MetaImageFile();
    Assert.That(file.IsCompressed, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void MetaImageFile_DefaultElementType_IsMetUChar() {
    var file = new MetaImageFile();
    Assert.That(file.ElementType, Is.EqualTo(MetaImageElementType.MetUChar));
  }

  [Test]
  [Category("Unit")]
  public void MetaImageFile_DefaultWidth_IsZero() {
    var file = new MetaImageFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void MetaImageFile_DefaultHeight_IsZero() {
    var file = new MetaImageFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void MetaImageFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80 };
    var file = new MetaImageFile {
      Width = 3,
      Height = 1,
      ElementType = MetaImageElementType.MetUChar,
      Channels = 1,
      IsCompressed = true,
      PixelData = pixels,
    };

    Assert.That(file.Width, Is.EqualTo(3));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.ElementType, Is.EqualTo(MetaImageElementType.MetUChar));
    Assert.That(file.Channels, Is.EqualTo(1));
    Assert.That(file.IsCompressed, Is.True);
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void MetaImageFile_PrimaryExtension_IsMha() {
    var ext = GetPrimaryExtension();
    Assert.That(ext, Is.EqualTo(".mha"));
  }

  [Test]
  [Category("Unit")]
  public void MetaImageFile_FileExtensions_ContainsBothExtensions() {
    var exts = GetFileExtensions();
    Assert.That(exts, Has.Length.EqualTo(2));
    Assert.That(exts, Does.Contain(".mha"));
    Assert.That(exts, Does.Contain(".mhd"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MetaImageFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MetaImageFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_MetShort_ThrowsNotSupportedException() {
    var file = new MetaImageFile {
      Width = 1,
      Height = 1,
      ElementType = MetaImageElementType.MetShort,
      PixelData = new byte[2],
    };
    Assert.Throws<NotSupportedException>(() => MetaImageFile.ToRawImage(file));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_UnsupportedFormat_ThrowsNotSupportedException() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgba32,
      PixelData = new byte[4],
    };
    Assert.Throws<NotSupportedException>(() => MetaImageFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Gray8_ReturnsCorrectFormat() {
    var file = new MetaImageFile {
      Width = 1,
      Height = 1,
      ElementType = MetaImageElementType.MetUChar,
      Channels = 1,
      PixelData = [0x42],
    };
    var raw = MetaImageFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Rgb24_ReturnsCorrectFormat() {
    var file = new MetaImageFile {
      Width = 1,
      Height = 1,
      ElementType = MetaImageElementType.MetUChar,
      Channels = 3,
      PixelData = [0x10, 0x20, 0x30],
    };
    var raw = MetaImageFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var file = new MetaImageFile {
      Width = 1,
      Height = 1,
      ElementType = MetaImageElementType.MetUChar,
      Channels = 1,
      PixelData = [0xAB],
    };
    var raw = MetaImageFile.ToRawImage(file);
    Assert.That(raw.PixelData, Is.Not.SameAs(file.PixelData));
    Assert.That(raw.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPixelData() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Gray8,
      PixelData = [0xCD],
    };
    var file = MetaImageFile.FromRawImage(raw);
    Assert.That(file.PixelData, Is.Not.SameAs(raw.PixelData));
    Assert.That(file.PixelData, Is.EqualTo(raw.PixelData));
  }

  // Helper to access static interface members
  private static string GetPrimaryExtension() => Access<string>(nameof(IImageFileFormat<MetaImageFile>.PrimaryExtension));
  private static string[] GetFileExtensions() => Access<string[]>(nameof(IImageFileFormat<MetaImageFile>.FileExtensions));

  private static T Access<T>(string propertyName) {
    var prop = typeof(MetaImageFile).GetProperty(propertyName, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
    if (prop != null)
      return (T)prop.GetValue(null)!;

    // Try explicit interface implementation via interface map
    var iface = typeof(IImageFileFormat<MetaImageFile>);
    var iProp = iface.GetProperty(propertyName);
    if (iProp == null)
      throw new InvalidOperationException($"Property {propertyName} not found.");

    var getter = iProp.GetGetMethod()!;
    var map = typeof(MetaImageFile).GetInterfaceMap(iface);
    for (var i = 0; i < map.InterfaceMethods.Length; ++i)
      if (map.InterfaceMethods[i] == getter)
        return (T)map.TargetMethods[i].Invoke(null, null)!;

    throw new InvalidOperationException($"Property {propertyName} not found in interface map.");
  }
}
