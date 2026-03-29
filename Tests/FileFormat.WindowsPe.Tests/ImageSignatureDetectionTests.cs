using System;
using FileFormat.WindowsPe;

namespace FileFormat.WindowsPe.Tests;

[TestFixture]
public sealed class ImageSignatureDetectionTests {

  [Test]
  [Category("Unit")]
  public void DetectImageSignature_PngSignature_ReturnsPng() {
    var data = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00 };
    var result = PeResourceReader._DetectImageSignature(data, 0, data.Length);
    Assert.That(result, Is.EqualTo("png"));
  }

  [Test]
  [Category("Unit")]
  public void DetectImageSignature_JpegSignature_ReturnsJpeg() {
    var data = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
    var result = PeResourceReader._DetectImageSignature(data, 0, data.Length);
    Assert.That(result, Is.EqualTo("jpeg"));
  }

  [Test]
  [Category("Unit")]
  public void DetectImageSignature_Gif87a_ReturnsGif() {
    var data = new byte[] { 0x47, 0x49, 0x46, 0x38, 0x37, 0x61, 0x00, 0x00 };
    var result = PeResourceReader._DetectImageSignature(data, 0, data.Length);
    Assert.That(result, Is.EqualTo("gif"));
  }

  [Test]
  [Category("Unit")]
  public void DetectImageSignature_Gif89a_ReturnsGif() {
    var data = new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x00, 0x00 };
    var result = PeResourceReader._DetectImageSignature(data, 0, data.Length);
    Assert.That(result, Is.EqualTo("gif"));
  }

  [Test]
  [Category("Unit")]
  public void DetectImageSignature_BmpSignature_ReturnsBmp() {
    var data = new byte[] { 0x42, 0x4D, 0x00, 0x00, 0x00, 0x00 };
    var result = PeResourceReader._DetectImageSignature(data, 0, data.Length);
    Assert.That(result, Is.EqualTo("bmp"));
  }

  [Test]
  [Category("Unit")]
  public void DetectImageSignature_TiffLE_ReturnsTiff() {
    var data = new byte[] { 0x49, 0x49, 0x2A, 0x00, 0x08, 0x00 };
    var result = PeResourceReader._DetectImageSignature(data, 0, data.Length);
    Assert.That(result, Is.EqualTo("tiff"));
  }

  [Test]
  [Category("Unit")]
  public void DetectImageSignature_TiffBE_ReturnsTiff() {
    var data = new byte[] { 0x4D, 0x4D, 0x00, 0x2A, 0x00, 0x08 };
    var result = PeResourceReader._DetectImageSignature(data, 0, data.Length);
    Assert.That(result, Is.EqualTo("tiff"));
  }

  [Test]
  [Category("Unit")]
  public void DetectImageSignature_WebP_ReturnsWebP() {
    var data = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50 };
    var result = PeResourceReader._DetectImageSignature(data, 0, data.Length);
    Assert.That(result, Is.EqualTo("webp"));
  }

  [Test]
  [Category("Unit")]
  public void DetectImageSignature_Ico_ReturnsIco() {
    var data = new byte[] { 0x00, 0x00, 0x01, 0x00, 0x01, 0x00 };
    var result = PeResourceReader._DetectImageSignature(data, 0, data.Length);
    Assert.That(result, Is.EqualTo("ico"));
  }

  [Test]
  [Category("Unit")]
  public void DetectImageSignature_Cur_ReturnsCur() {
    var data = new byte[] { 0x00, 0x00, 0x02, 0x00, 0x01, 0x00 };
    var result = PeResourceReader._DetectImageSignature(data, 0, data.Length);
    Assert.That(result, Is.EqualTo("cur"));
  }

  [Test]
  [Category("Unit")]
  public void DetectImageSignature_UnknownData_ReturnsNull() {
    var data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x00 };
    var result = PeResourceReader._DetectImageSignature(data, 0, data.Length);
    Assert.That(result, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void DetectImageSignature_TooSmall_ReturnsNull() {
    var data = new byte[] { 0x89, 0x50 };
    var result = PeResourceReader._DetectImageSignature(data, 0, data.Length);
    Assert.That(result, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void DetectImageSignature_WithOffset_DetectsCorrectly() {
    var data = new byte[16];
    data[4] = 0x42;
    data[5] = 0x4D;
    var result = PeResourceReader._DetectImageSignature(data, 4, 6);
    Assert.That(result, Is.EqualTo("bmp"));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_WithEmbeddedPng_DetectsAsPng() {
    var pngSig = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D };
    var pe = MinimalPeBuilder.BuildWithEmbeddedImage(pngSig);
    var file = PeResourceReader.FromBytes(pe);
    Assert.That(file.ImageResources, Has.Count.EqualTo(1));
    Assert.That(file.ImageResources[0].FormatHint, Is.EqualTo("png"));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_WithEmbeddedJpeg_DetectsAsJpeg() {
    var jpegSig = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };
    var pe = MinimalPeBuilder.BuildWithEmbeddedImage(jpegSig);
    var file = PeResourceReader.FromBytes(pe);
    Assert.That(file.ImageResources, Has.Count.EqualTo(1));
    Assert.That(file.ImageResources[0].FormatHint, Is.EqualTo("jpeg"));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_WithNonImageRcdata_NoImageResources() {
    var randomData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03, 0x04 };
    var pe = MinimalPeBuilder.BuildWithEmbeddedImage(randomData);
    var file = PeResourceReader.FromBytes(pe);
    Assert.That(file.ImageResources, Is.Empty);
  }
}
