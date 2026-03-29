using System;
using System.IO;
using FileFormat.Pdf;

namespace FileFormat.Pdf.Tests;

[TestFixture]
public sealed class PdfReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => PdfReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_MissingFile_Throws()
    => Assert.Throws<FileNotFoundException>(() => PdfReader.FromFile(new FileInfo("nonexistent.pdf")));

  [Test]
  [Category("Unit")]
  public void FromBytes_NullData_Throws()
    => Assert.Throws<ArgumentNullException>(() => PdfReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_Throws()
    => Assert.Throws<InvalidDataException>(() => PdfReader.FromBytes(new byte[10]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidSignature_Throws()
    => Assert.Throws<InvalidDataException>(() => PdfReader.FromBytes(new byte[128]));

  [Test]
  [Category("Unit")]
  public void FromStream_NullStream_Throws()
    => Assert.Throws<ArgumentNullException>(() => PdfReader.FromStream(null!));

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidPdf_ParsesWithoutError() {
    var image = new PdfImage {
      Width = 2, Height = 2, BitsPerComponent = 8,
      ColorSpace = PdfColorSpace.DeviceRGB,
      PixelData = new byte[12],
    };
    var pdfFile = new PdfFile { Images = [image] };
    var data = PdfWriter.ToBytes(pdfFile);
    var restored = PdfReader.FromBytes(data);
    Assert.That(restored, Is.Not.Null);
    Assert.That(restored.Images, Is.Not.Null);
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_SingleRgbImage_ExtractsOneImage() {
    var pixels = new byte[12];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 17 % 256);
    var image = new PdfImage {
      Width = 2, Height = 2, BitsPerComponent = 8,
      ColorSpace = PdfColorSpace.DeviceRGB,
      PixelData = pixels,
    };
    var data = PdfWriter.ToBytes(new PdfFile { Images = [image] });
    var restored = PdfReader.FromBytes(data);
    Assert.That(restored.Images.Count, Is.GreaterThanOrEqualTo(1));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_SingleRgbImage_ExtractsCorrectDimensions() {
    var image = new PdfImage {
      Width = 4, Height = 3, BitsPerComponent = 8,
      ColorSpace = PdfColorSpace.DeviceRGB,
      PixelData = new byte[36],
    };
    var data = PdfWriter.ToBytes(new PdfFile { Images = [image] });
    var restored = PdfReader.FromBytes(data);
    Assert.That(restored.Images.Count, Is.GreaterThanOrEqualTo(1));
    Assert.Multiple(() => {
      Assert.That(restored.Images[0].Width, Is.EqualTo(4));
      Assert.That(restored.Images[0].Height, Is.EqualTo(3));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_GrayscaleImage_ExtractsGrayColorSpace() {
    var image = new PdfImage {
      Width = 4, Height = 3, BitsPerComponent = 8,
      ColorSpace = PdfColorSpace.DeviceGray,
      PixelData = new byte[12],
    };
    var data = PdfWriter.ToBytes(new PdfFile { Images = [image] });
    var restored = PdfReader.FromBytes(data);
    Assert.That(restored.Images.Count, Is.GreaterThanOrEqualTo(1));
    Assert.That(restored.Images[0].ColorSpace, Is.EqualTo(PdfColorSpace.DeviceGray));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_EmptyPdf_ExtractsNoImages() {
    var data = PdfWriter.ToBytes(new PdfFile { Images = [] });
    var restored = PdfReader.FromBytes(data);
    Assert.That(restored.Images.Count, Is.EqualTo(0));
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidData_Parses() {
    var image = new PdfImage {
      Width = 2, Height = 2, BitsPerComponent = 8,
      ColorSpace = PdfColorSpace.DeviceRGB,
      PixelData = new byte[12],
    };
    var data = PdfWriter.ToBytes(new PdfFile { Images = [image] });
    using var ms = new MemoryStream(data);
    var restored = PdfReader.FromStream(ms);
    Assert.That(restored.Images.Count, Is.GreaterThanOrEqualTo(1));
  }

  [Test]
  [Category("Integration")]
  public void FromFile_ValidPdf_Parses() {
    var image = new PdfImage {
      Width = 2, Height = 2, BitsPerComponent = 8,
      ColorSpace = PdfColorSpace.DeviceRGB,
      PixelData = new byte[12],
    };
    var data = PdfWriter.ToBytes(new PdfFile { Images = [image] });
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf");
    try {
      File.WriteAllBytes(tempPath, data);
      var restored = PdfReader.FromFile(new FileInfo(tempPath));
      Assert.That(restored.Images.Count, Is.GreaterThanOrEqualTo(1));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_MultipleImages_ExtractsAll() {
    var image1 = new PdfImage {
      Width = 2, Height = 2, BitsPerComponent = 8,
      ColorSpace = PdfColorSpace.DeviceRGB,
      PixelData = new byte[12],
    };
    var image2 = new PdfImage {
      Width = 3, Height = 3, BitsPerComponent = 8,
      ColorSpace = PdfColorSpace.DeviceGray,
      PixelData = new byte[9],
    };
    var data = PdfWriter.ToBytes(new PdfFile { Images = [image1, image2] });
    var restored = PdfReader.FromBytes(data);
    Assert.That(restored.Images.Count, Is.GreaterThanOrEqualTo(2));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ExtractsPixelData() {
    var pixels = new byte[12];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 21 % 256);
    var image = new PdfImage {
      Width = 2, Height = 2, BitsPerComponent = 8,
      ColorSpace = PdfColorSpace.DeviceRGB,
      PixelData = pixels,
    };
    var data = PdfWriter.ToBytes(new PdfFile { Images = [image] });
    var restored = PdfReader.FromBytes(data);
    Assert.That(restored.Images.Count, Is.GreaterThanOrEqualTo(1));
    Assert.That(restored.Images[0].PixelData.Length, Is.GreaterThan(0));
  }
}
