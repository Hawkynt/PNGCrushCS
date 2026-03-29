using System;
using System.Text;
using FileFormat.Pdf;

namespace FileFormat.Pdf.Tests;

[TestFixture]
public sealed class PdfWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => PdfWriter.ToBytes(null!));

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesPdfSignature() {
    var data = PdfWriter.ToBytes(new PdfFile());
    Assert.Multiple(() => {
      Assert.That(data[0], Is.EqualTo((byte)'%'));
      Assert.That(data[1], Is.EqualTo((byte)'P'));
      Assert.That(data[2], Is.EqualTo((byte)'D'));
      Assert.That(data[3], Is.EqualTo((byte)'F'));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesVersionHeader() {
    var data = PdfWriter.ToBytes(new PdfFile());
    var header = Encoding.ASCII.GetString(data, 0, 8);
    Assert.That(header, Is.EqualTo("%PDF-1.4"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesBinaryComment() {
    var data = PdfWriter.ToBytes(new PdfFile());
    Assert.Multiple(() => {
      Assert.That(data[9], Is.EqualTo((byte)'%'));
      Assert.That(data[10], Is.GreaterThan(0x80));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsEofMarker() {
    var data = PdfWriter.ToBytes(new PdfFile());
    var text = Encoding.ASCII.GetString(data);
    Assert.That(text, Does.Contain("%%EOF"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsStartxref() {
    var data = PdfWriter.ToBytes(new PdfFile());
    var text = Encoding.ASCII.GetString(data);
    Assert.That(text, Does.Contain("startxref"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsXrefSection() {
    var data = PdfWriter.ToBytes(new PdfFile());
    var text = Encoding.ASCII.GetString(data);
    Assert.That(text, Does.Contain("xref"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsTrailer() {
    var data = PdfWriter.ToBytes(new PdfFile());
    var text = Encoding.ASCII.GetString(data);
    Assert.That(text, Does.Contain("trailer"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsCatalog() {
    var data = PdfWriter.ToBytes(new PdfFile());
    var text = Encoding.ASCII.GetString(data);
    Assert.That(text, Does.Contain("/Catalog"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsPagesObject() {
    var data = PdfWriter.ToBytes(new PdfFile());
    var text = Encoding.ASCII.GetString(data);
    Assert.That(text, Does.Contain("/Pages"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EmptyPdf_ProducesValidOutput() {
    var data = PdfWriter.ToBytes(new PdfFile { Images = [] });
    Assert.That(data.Length, Is.GreaterThan(0));
    var header = Encoding.ASCII.GetString(data, 0, 4);
    Assert.That(header, Is.EqualTo("%PDF"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SingleImage_ContainsImageXObject() {
    var image = new PdfImage {
      Width = 2, Height = 2, BitsPerComponent = 8,
      ColorSpace = PdfColorSpace.DeviceRGB,
      PixelData = new byte[12],
    };
    var data = PdfWriter.ToBytes(new PdfFile { Images = [image] });
    var text = Encoding.ASCII.GetString(data);
    Assert.That(text, Does.Contain("/Subtype /Image"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SingleImage_ContainsWidth() {
    var image = new PdfImage {
      Width = 7, Height = 5, BitsPerComponent = 8,
      ColorSpace = PdfColorSpace.DeviceRGB,
      PixelData = new byte[105],
    };
    var data = PdfWriter.ToBytes(new PdfFile { Images = [image] });
    var text = Encoding.ASCII.GetString(data);
    Assert.That(text, Does.Contain("/Width 7"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SingleImage_ContainsHeight() {
    var image = new PdfImage {
      Width = 7, Height = 5, BitsPerComponent = 8,
      ColorSpace = PdfColorSpace.DeviceRGB,
      PixelData = new byte[105],
    };
    var data = PdfWriter.ToBytes(new PdfFile { Images = [image] });
    var text = Encoding.ASCII.GetString(data);
    Assert.That(text, Does.Contain("/Height 5"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RgbImage_ContainsDeviceRGBColorSpace() {
    var image = new PdfImage {
      Width = 2, Height = 2, BitsPerComponent = 8,
      ColorSpace = PdfColorSpace.DeviceRGB,
      PixelData = new byte[12],
    };
    var data = PdfWriter.ToBytes(new PdfFile { Images = [image] });
    var text = Encoding.ASCII.GetString(data);
    Assert.That(text, Does.Contain("/DeviceRGB"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_GrayImage_ContainsDeviceGrayColorSpace() {
    var image = new PdfImage {
      Width = 2, Height = 2, BitsPerComponent = 8,
      ColorSpace = PdfColorSpace.DeviceGray,
      PixelData = new byte[4],
    };
    var data = PdfWriter.ToBytes(new PdfFile { Images = [image] });
    var text = Encoding.ASCII.GetString(data);
    Assert.That(text, Does.Contain("/DeviceGray"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SingleImage_ContainsFlateDecode() {
    var image = new PdfImage {
      Width = 2, Height = 2, BitsPerComponent = 8,
      ColorSpace = PdfColorSpace.DeviceRGB,
      PixelData = new byte[12],
    };
    var data = PdfWriter.ToBytes(new PdfFile { Images = [image] });
    var text = Encoding.ASCII.GetString(data);
    Assert.That(text, Does.Contain("/FlateDecode"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SingleImage_ContainsBitsPerComponent() {
    var image = new PdfImage {
      Width = 2, Height = 2, BitsPerComponent = 8,
      ColorSpace = PdfColorSpace.DeviceRGB,
      PixelData = new byte[12],
    };
    var data = PdfWriter.ToBytes(new PdfFile { Images = [image] });
    var text = Encoding.ASCII.GetString(data);
    Assert.That(text, Does.Contain("/BitsPerComponent 8"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MultipleImages_ContainsMultiplePages() {
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
    var text = Encoding.ASCII.GetString(data);
    Assert.That(text, Does.Contain("/Count 2"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SingleImage_OutputIsLargerThanHeader() {
    var image = new PdfImage {
      Width = 2, Height = 2, BitsPerComponent = 8,
      ColorSpace = PdfColorSpace.DeviceRGB,
      PixelData = new byte[12],
    };
    var data = PdfWriter.ToBytes(new PdfFile { Images = [image] });
    Assert.That(data.Length, Is.GreaterThan(100));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CmykImage_ContainsDeviceCMYKColorSpace() {
    var image = new PdfImage {
      Width = 2, Height = 2, BitsPerComponent = 8,
      ColorSpace = PdfColorSpace.DeviceCMYK,
      PixelData = new byte[16],
    };
    var data = PdfWriter.ToBytes(new PdfFile { Images = [image] });
    var text = Encoding.ASCII.GetString(data);
    Assert.That(text, Does.Contain("/DeviceCMYK"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsContentStream() {
    var image = new PdfImage {
      Width = 2, Height = 2, BitsPerComponent = 8,
      ColorSpace = PdfColorSpace.DeviceRGB,
      PixelData = new byte[12],
    };
    var data = PdfWriter.ToBytes(new PdfFile { Images = [image] });
    var text = Encoding.ASCII.GetString(data);
    Assert.That(text, Does.Contain("/Contents"));
  }
}
