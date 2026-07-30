using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Pdf;

namespace FileFormat.Pdf.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb24_PreservesPixelData() {
    var pixels = new byte[12];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 21 % 256);
    var original = _MakeSingleImagePdf(2, 2, PdfColorSpace.DeviceRGB, pixels);
    var data = PdfWriter.ToBytes(original);
    var restored = PdfReader.FromBytes(data);
    Assert.That(restored.Images.Count, Is.GreaterThanOrEqualTo(1));
    Assert.That(restored.Images[0].PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gray8_PreservesPixelData() {
    var pixels = new byte[6];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 40);
    var original = _MakeSingleImagePdf(3, 2, PdfColorSpace.DeviceGray, pixels);
    var data = PdfWriter.ToBytes(original);
    var restored = PdfReader.FromBytes(data);
    Assert.That(restored.Images.Count, Is.GreaterThanOrEqualTo(1));
    Assert.That(restored.Images[0].PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PreservesDimensions() {
    var original = _MakeSingleImagePdf(8, 6, PdfColorSpace.DeviceRGB, new byte[144]);
    var data = PdfWriter.ToBytes(original);
    var restored = PdfReader.FromBytes(data);
    Assert.That(restored.Images.Count, Is.GreaterThanOrEqualTo(1));
    Assert.Multiple(() => {
      Assert.That(restored.Images[0].Width, Is.EqualTo(8));
      Assert.That(restored.Images[0].Height, Is.EqualTo(6));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PreservesColorSpace() {
    var original = _MakeSingleImagePdf(2, 2, PdfColorSpace.DeviceGray, new byte[4]);
    var data = PdfWriter.ToBytes(original);
    var restored = PdfReader.FromBytes(data);
    Assert.That(restored.Images.Count, Is.GreaterThanOrEqualTo(1));
    Assert.That(restored.Images[0].ColorSpace, Is.EqualTo(PdfColorSpace.DeviceGray));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixels = new byte[12];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 13 % 256);
    var original = _MakeSingleImagePdf(2, 2, PdfColorSpace.DeviceRGB, pixels);
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf");
    try {
      File.WriteAllBytes(tempPath, PdfWriter.ToBytes(original));
      var restored = PdfReader.FromFile(new FileInfo(tempPath));
      Assert.That(restored.Images.Count, Is.GreaterThanOrEqualTo(1));
      Assert.That(restored.Images[0].PixelData, Is.EqualTo(pixels));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaStream() {
    var pixels = new byte[12];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 7 % 256);
    var original = _MakeSingleImagePdf(2, 2, PdfColorSpace.DeviceRGB, pixels);
    var bytes = PdfWriter.ToBytes(original);
    using var ms = new MemoryStream(bytes);
    var restored = PdfReader.FromStream(ms);
    Assert.That(restored.Images.Count, Is.GreaterThanOrEqualTo(1));
    Assert.That(restored.Images[0].PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = _MakeSingleImagePdf(4, 4, PdfColorSpace.DeviceRGB, new byte[48]);
    var data = PdfWriter.ToBytes(original);
    var restored = PdfReader.FromBytes(data);
    Assert.That(restored.Images.Count, Is.GreaterThanOrEqualTo(1));
    Assert.That(restored.Images[0].PixelData, Is.EqualTo(original.Images[0].PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultipleImages_PreservesCount() {
    var original = new PdfFile {
      Images = [
        new PdfImage { Width = 3, Height = 2, BitsPerComponent = 8, ColorSpace = PdfColorSpace.DeviceRGB, PixelData = new byte[18] },
        new PdfImage { Width = 4, Height = 5, BitsPerComponent = 8, ColorSpace = PdfColorSpace.DeviceGray, PixelData = new byte[20] },
      ],
    };
    var data = PdfWriter.ToBytes(original);
    var restored = PdfReader.FromBytes(data);
    Assert.That(restored.Images.Count, Is.GreaterThanOrEqualTo(2));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_EmptyPdf() {
    var data = PdfWriter.ToBytes(new PdfFile { Images = [] });
    var restored = PdfReader.FromBytes(data);
    Assert.That(restored.Images.Count, Is.EqualTo(0));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Rgb24() {
    var raw = new RawImage { Width = 4, Height = 3, Format = PixelFormat.Rgb24, PixelData = new byte[36] };
    raw.PixelData[0] = 255;
    raw.PixelData[11] = 128;
    var file = PdfFile.FromRawImage(raw);
    var rawBack = PdfFile.ToRawImage(file);
    Assert.Multiple(() => {
      Assert.That(rawBack.Width, Is.EqualTo(4));
      Assert.That(rawBack.Height, Is.EqualTo(3));
      Assert.That(rawBack.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(rawBack.PixelData[0], Is.EqualTo(255));
      Assert.That(rawBack.PixelData[11], Is.EqualTo(128));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Gray8() {
    var raw = new RawImage { Width = 3, Height = 2, Format = PixelFormat.Gray8, PixelData = new byte[6] };
    raw.PixelData[0] = 100;
    raw.PixelData[5] = 200;
    var file = PdfFile.FromRawImage(raw);
    var rawBack = PdfFile.ToRawImage(file);
    Assert.Multiple(() => {
      Assert.That(rawBack.Width, Is.EqualTo(3));
      Assert.That(rawBack.Height, Is.EqualTo(2));
      Assert.That(rawBack.Format, Is.EqualTo(PixelFormat.Gray8));
      Assert.That(rawBack.PixelData[0], Is.EqualTo(100));
      Assert.That(rawBack.PixelData[5], Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargerImage() {
    var width = 32;
    var height = 24;
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 13 % 256);
    var original = _MakeSingleImagePdf(width, height, PdfColorSpace.DeviceRGB, pixels);
    var data = PdfWriter.ToBytes(original);
    var restored = PdfReader.FromBytes(data);
    Assert.That(restored.Images.Count, Is.GreaterThanOrEqualTo(1));
    Assert.Multiple(() => {
      Assert.That(restored.Images[0].Width, Is.EqualTo(width));
      Assert.That(restored.Images[0].Height, Is.EqualTo(height));
      Assert.That(restored.Images[0].PixelData, Is.EqualTo(pixels));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_FullWriteReadCycle_PreservesBytes() {
    var pixels = new byte[] { 0, 64, 128, 192, 255, 33 };
    var original = _MakeSingleImagePdf(2, 1, PdfColorSpace.DeviceRGB, pixels);
    var data = PdfWriter.ToBytes(original);
    var restored = PdfReader.FromBytes(data);
    Assert.That(restored.Images.Count, Is.GreaterThanOrEqualTo(1));
    Assert.That(restored.Images[0].PixelData, Is.EqualTo(pixels));
  }

  private static PdfFile _MakeSingleImagePdf(int w, int h, PdfColorSpace cs, byte[] pixels) => new() {
    Images = [new PdfImage {
      Width = w, Height = h, BitsPerComponent = 8,
      ColorSpace = cs, PixelData = pixels,
    }],
  };
}
