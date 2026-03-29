using System;
using FileFormat.Flif;
using FileFormat.Core;

namespace FileFormat.Flif.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void FlifChannelCount_HasExpectedValues() {
    Assert.That((byte)FlifChannelCount.Gray, Is.EqualTo(1));
    Assert.That((byte)FlifChannelCount.Rgb, Is.EqualTo(3));
    Assert.That((byte)FlifChannelCount.Rgba, Is.EqualTo(4));

    var values = Enum.GetValues<FlifChannelCount>();
    Assert.That(values, Has.Length.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void FlifFile_DefaultPixelData_IsEmptyArray() {
    var file = new FlifFile { Width = 1, Height = 1 };
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Has.Length.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FlifFile_DefaultChannelCount_IsRgb() {
    var file = new FlifFile { Width = 1, Height = 1 };
    Assert.That(file.ChannelCount, Is.EqualTo(FlifChannelCount.Rgb));
  }

  [Test]
  [Category("Unit")]
  public void FlifFile_DefaultBitsPerChannel_Is8() {
    var file = new FlifFile { Width = 1, Height = 1 };
    Assert.That(file.BitsPerChannel, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FlifFile_DefaultIsInterlaced_IsFalse() {
    var file = new FlifFile { Width = 1, Height = 1 };
    Assert.That(file.IsInterlaced, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void FlifFile_DefaultIsAnimated_IsFalse() {
    var file = new FlifFile { Width = 1, Height = 1 };
    Assert.That(file.IsAnimated, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void FlifFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 1, 2, 3 };
    var file = new FlifFile {
      Width = 10,
      Height = 20,
      ChannelCount = FlifChannelCount.Rgba,
      BitsPerChannel = 8,
      IsInterlaced = true,
      IsAnimated = false,
      PixelData = pixels
    };

    Assert.That(file.Width, Is.EqualTo(10));
    Assert.That(file.Height, Is.EqualTo(20));
    Assert.That(file.ChannelCount, Is.EqualTo(FlifChannelCount.Rgba));
    Assert.That(file.BitsPerChannel, Is.EqualTo(8));
    Assert.That(file.IsInterlaced, Is.True);
    Assert.That(file.IsAnimated, Is.False);
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FlifFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FlifFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_UnsupportedFormat_ThrowsArgumentException() {
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Bgra32,
      PixelData = new byte[4]
    };

    Assert.Throws<ArgumentException>(() => FlifFile.FromRawImage(image));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Gray_ReturnsGray8Format() {
    var file = new FlifFile {
      Width = 1,
      Height = 1,
      ChannelCount = FlifChannelCount.Gray,
      BitsPerChannel = 8,
      PixelData = new byte[] { 128 }
    };

    var raw = FlifFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Rgb_ReturnsRgb24Format() {
    var file = new FlifFile {
      Width = 1,
      Height = 1,
      ChannelCount = FlifChannelCount.Rgb,
      BitsPerChannel = 8,
      PixelData = new byte[] { 1, 2, 3 }
    };

    var raw = FlifFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Rgba_ReturnsRgba32Format() {
    var file = new FlifFile {
      Width = 1,
      Height = 1,
      ChannelCount = FlifChannelCount.Rgba,
      BitsPerChannel = 8,
      PixelData = new byte[] { 1, 2, 3, 4 }
    };

    var raw = FlifFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgba32));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_PixelData_IsCloned() {
    var pixels = new byte[] { 1, 2, 3 };
    var file = new FlifFile {
      Width = 1,
      Height = 1,
      ChannelCount = FlifChannelCount.Rgb,
      BitsPerChannel = 8,
      PixelData = pixels
    };

    var raw = FlifFile.ToRawImage(file);

    Assert.That(raw.PixelData, Is.Not.SameAs(pixels));
    Assert.That(raw.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_PixelData_IsCloned() {
    var pixels = new byte[] { 10, 20, 30 };
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = pixels
    };

    var flif = FlifFile.FromRawImage(image);

    Assert.That(flif.PixelData, Is.Not.SameAs(pixels));
    Assert.That(flif.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsFlif() {
    var ext = GetPrimaryExtension();
    Assert.That(ext, Is.EqualTo(".flif"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsFlif() {
    var exts = GetFileExtensions();
    Assert.That(exts, Contains.Item(".flif"));
    Assert.That(exts, Has.Length.EqualTo(1));
  }

  private static string GetPrimaryExtension() {
    // Access via interface to verify implementation
    return CallStatic<string>(nameof(IImageFileFormat<FlifFile>.PrimaryExtension));
  }

  private static string[] GetFileExtensions() {
    return CallStatic<string[]>(nameof(IImageFileFormat<FlifFile>.FileExtensions));
  }

  private static T CallStatic<T>(string propertyName) {
    var prop = typeof(FlifFile).GetInterfaceMap(typeof(IImageFileFormat<FlifFile>))
      .TargetMethods;
    foreach (var method in prop)
      if (method.Name.Contains(propertyName))
        return (T)method.Invoke(null, null)!;
    throw new InvalidOperationException($"Property {propertyName} not found.");
  }
}
