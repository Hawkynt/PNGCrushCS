using System;
using FileFormat.Core;
using FileFormat.Pdf;

namespace FileFormat.Pdf.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void PdfColorSpace_DeviceRGB_IsZero()
    => Assert.That((int)PdfColorSpace.DeviceRGB, Is.EqualTo(0));

  [Test]
  [Category("Unit")]
  public void PdfColorSpace_DeviceGray_IsOne()
    => Assert.That((int)PdfColorSpace.DeviceGray, Is.EqualTo(1));

  [Test]
  [Category("Unit")]
  public void PdfColorSpace_DeviceCMYK_IsTwo()
    => Assert.That((int)PdfColorSpace.DeviceCMYK, Is.EqualTo(2));

  [Test]
  [Category("Unit")]
  public void PdfColorSpace_HasThreeValues()
    => Assert.That(Enum.GetValues<PdfColorSpace>(), Has.Length.EqualTo(3));

  [Test]
  [Category("Unit")]
  public void PdfImage_Defaults_AreCorrect() {
    var image = new PdfImage();
    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(0));
      Assert.That(image.Height, Is.EqualTo(0));
      Assert.That(image.BitsPerComponent, Is.EqualTo(8));
      Assert.That(image.ColorSpace, Is.EqualTo(PdfColorSpace.DeviceRGB));
      Assert.That(image.PixelData, Is.Empty);
    });
  }

  [Test]
  [Category("Unit")]
  public void PdfImage_InitProperties_RoundTrip() {
    var pixels = new byte[] { 1, 2, 3, 4, 5, 6 };
    var image = new PdfImage {
      Width = 2,
      Height = 1,
      BitsPerComponent = 8,
      ColorSpace = PdfColorSpace.DeviceRGB,
      PixelData = pixels,
    };
    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(2));
      Assert.That(image.Height, Is.EqualTo(1));
      Assert.That(image.BitsPerComponent, Is.EqualTo(8));
      Assert.That(image.ColorSpace, Is.EqualTo(PdfColorSpace.DeviceRGB));
      Assert.That(image.PixelData, Is.SameAs(pixels));
    });
  }

  [Test]
  [Category("Unit")]
  public void PdfFile_Defaults_AreCorrect() {
    var file = new PdfFile();
    Assert.That(file.Images, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void PdfFile_InitImages() {
    var image = new PdfImage { Width = 1, Height = 1, PixelData = new byte[3] };
    var file = new PdfFile { Images = [image] };
    Assert.That(file.Images.Count, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void Capabilities_IncludesVariableResolution() {
    var caps = _GetStaticProperty<FormatCapability>("Capabilities");
    Assert.That(caps.HasFlag(FormatCapability.VariableResolution), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void Capabilities_IncludesMultiImage() {
    var caps = _GetStaticProperty<FormatCapability>("Capabilities");
    Assert.That(caps.HasFlag(FormatCapability.MultiImage), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => PdfFile.ToRawImage(null!));

  [Test]
  [Category("Unit")]
  public void FromRawImage_NullImage_Throws()
    => Assert.Throws<ArgumentNullException>(() => PdfFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void FromRawImage_InvalidFormat_Throws()
    => Assert.Throws<ArgumentException>(() => PdfFile.FromRawImage(new RawImage {
      Width = 2, Height = 2, Format = PixelFormat.Rgba32, PixelData = new byte[16],
    }));

  [Test]
  [Category("Unit")]
  public void ToRawImage_NoImages_Throws() {
    var file = new PdfFile { Images = [] };
    Assert.Throws<InvalidOperationException>(() => PdfFile.ToRawImage(file));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_RgbImage_ReturnsRgb24() {
    var file = new PdfFile {
      Images = [new PdfImage {
        Width = 2, Height = 2, BitsPerComponent = 8,
        ColorSpace = PdfColorSpace.DeviceRGB,
        PixelData = new byte[12],
      }],
    };
    var raw = PdfFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_GrayImage_ReturnsGray8() {
    var file = new PdfFile {
      Images = [new PdfImage {
        Width = 2, Height = 2, BitsPerComponent = 8,
        ColorSpace = PdfColorSpace.DeviceGray,
        PixelData = new byte[4],
      }],
    };
    var raw = PdfFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));
  }

  [Test]
  [Category("Unit")]
  public void ImageCount_ReturnsCorrectCount() {
    var file = new PdfFile {
      Images = [
        new PdfImage { Width = 1, Height = 1, PixelData = new byte[3] },
        new PdfImage { Width = 1, Height = 1, PixelData = new byte[3] },
      ],
    };
    Assert.That(PdfFile.ImageCount(file), Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ImageCount_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => PdfFile.ImageCount(null!));

  private static T _GetStaticProperty<T>(string name) {
    var map = typeof(PdfFile).GetInterfaceMap(typeof(IImageFileFormat<PdfFile>));
    foreach (var method in map.TargetMethods)
      if (method.Name.Contains(name))
        return (T)method.Invoke(null, null)!;
    throw new InvalidOperationException("Not found: " + name);
  }
}
