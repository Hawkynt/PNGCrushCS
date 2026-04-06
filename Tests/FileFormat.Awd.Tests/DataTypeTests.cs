using System;
using FileFormat.Awd;
using FileFormat.Core;

namespace FileFormat.Awd.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void AwdFile_DefaultPixelData_IsEmptyArray() {
    var file = new AwdFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData.Length, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void AwdFile_InitProperties_RoundTrip() {
    var pixelData = new byte[] { 0xFF, 0x00 };
    var file = new AwdFile {
      Width = 8,
      Height = 2,
      PixelData = pixelData,
    };

    Assert.That(file.Width, Is.EqualTo(8));
    Assert.That(file.Height, Is.EqualTo(2));
    Assert.That(file.PixelData, Is.SameAs(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void AwdFile_PrimaryExtension_IsAwd() {
    Assert.That(_GetPrimaryExtension<AwdFile>(), Is.EqualTo(".awd"));
  }

  [Test]
  [Category("Unit")]
  public void AwdFile_FileExtensions_ContainsAwd() {
    Assert.That(_GetFileExtensions<AwdFile>(), Does.Contain(".awd"));
  }

  [Test]
  [Category("Unit")]
  public void AwdFile_ToRawImage_Null_ThrowsNullReferenceException() {
  }

  [Test]
  [Category("Unit")]
  public void AwdFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AwdFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void AwdFile_FromRawImage_UnsupportedFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 8,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[24],
    };
    Assert.Throws<ArgumentException>(() => AwdFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void AwdFile_ToRawImage_ProducesIndexed1() {
    var file = new AwdFile {
      Width = 8,
      Height = 1,
      PixelData = new byte[] { 0b10101010 },
    };

    var raw = AwdFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
    Assert.That(raw.Width, Is.EqualTo(8));
    Assert.That(raw.Height, Is.EqualTo(1));
    Assert.That(raw.PaletteCount, Is.EqualTo(2));
    Assert.That(raw.Palette, Is.Not.Null);
    Assert.That(raw.Palette!.Length, Is.EqualTo(6));
  }

  [Test]
  [Category("Unit")]
  public void AwdFile_RawImage_RoundTrip() {
    var original = new AwdFile {
      Width = 8,
      Height = 2,
      PixelData = new byte[] { 0b11001100, 0b00110011 },
    };

    var raw = AwdFile.ToRawImage(original);
    var restored = AwdFile.FromRawImage(raw);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void AwdHeader_StructSize_Is16() {
    Assert.That(AwdHeader.StructSize, Is.EqualTo(16));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFormatMetadata<T> => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFormatMetadata<T> => T.FileExtensions;
}
