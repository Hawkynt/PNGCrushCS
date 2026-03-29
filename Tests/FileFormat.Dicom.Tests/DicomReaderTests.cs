using System;
using System.IO;
using FileFormat.Dicom;

namespace FileFormat.Dicom.Tests;

[TestFixture]
public sealed class DicomReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DicomReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DicomReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dcm"));
    Assert.Throws<FileNotFoundException>(() => DicomReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => DicomReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NoDicmMagic_ThrowsInvalidDataException() {
    var data = new byte[200];
    // 128-byte preamble present but no DICM magic
    Assert.Throws<InvalidDataException>(() => DicomReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidMono8_ParsesCorrectly() {
    var width = 4;
    var height = 3;
    var pixelData = new byte[width * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 21 % 256);

    var original = new DicomFile {
      Width = width,
      Height = height,
      BitsAllocated = 8,
      BitsStored = 8,
      SamplesPerPixel = 1,
      PhotometricInterpretation = DicomPhotometricInterpretation.Monochrome2,
      PixelData = pixelData,
      WindowCenter = 128,
      WindowWidth = 256
    };

    var bytes = DicomWriter.ToBytes(original);
    var result = DicomReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(width));
      Assert.That(result.Height, Is.EqualTo(height));
      Assert.That(result.BitsAllocated, Is.EqualTo(8));
      Assert.That(result.BitsStored, Is.EqualTo(8));
      Assert.That(result.SamplesPerPixel, Is.EqualTo(1));
      Assert.That(result.PhotometricInterpretation, Is.EqualTo(DicomPhotometricInterpretation.Monochrome2));
      Assert.That(result.PixelData, Is.EqualTo(pixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidMono16_ParsesCorrectly() {
    var width = 4;
    var height = 3;
    var pixelData = new byte[width * height * 2]; // 16 bits = 2 bytes per pixel
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new DicomFile {
      Width = width,
      Height = height,
      BitsAllocated = 16,
      BitsStored = 12,
      SamplesPerPixel = 1,
      PhotometricInterpretation = DicomPhotometricInterpretation.Monochrome2,
      PixelData = pixelData,
      WindowCenter = 2048,
      WindowWidth = 4096
    };

    var bytes = DicomWriter.ToBytes(original);
    var result = DicomReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(width));
      Assert.That(result.Height, Is.EqualTo(height));
      Assert.That(result.BitsAllocated, Is.EqualTo(16));
      Assert.That(result.BitsStored, Is.EqualTo(12));
      Assert.That(result.PixelData, Is.EqualTo(pixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesCorrectly() {
    var width = 2;
    var height = 2;
    var pixelData = new byte[width * height * 3]; // 3 samples per pixel
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 37 % 256);

    var original = new DicomFile {
      Width = width,
      Height = height,
      BitsAllocated = 8,
      BitsStored = 8,
      SamplesPerPixel = 3,
      PhotometricInterpretation = DicomPhotometricInterpretation.Rgb,
      PixelData = pixelData
    };

    var bytes = DicomWriter.ToBytes(original);
    var result = DicomReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(width));
      Assert.That(result.Height, Is.EqualTo(height));
      Assert.That(result.SamplesPerPixel, Is.EqualTo(3));
      Assert.That(result.PhotometricInterpretation, Is.EqualTo(DicomPhotometricInterpretation.Rgb));
      Assert.That(result.PixelData, Is.EqualTo(pixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var width = 2;
    var height = 2;
    var pixelData = new byte[width * height];
    pixelData[0] = 10; pixelData[1] = 20; pixelData[2] = 30; pixelData[3] = 40;

    var original = new DicomFile {
      Width = width,
      Height = height,
      BitsAllocated = 8,
      BitsStored = 8,
      SamplesPerPixel = 1,
      PhotometricInterpretation = DicomPhotometricInterpretation.Monochrome2,
      PixelData = pixelData
    };

    var bytes = DicomWriter.ToBytes(original);
    using var ms = new MemoryStream(bytes);
    var result = DicomReader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(width));
      Assert.That(result.Height, Is.EqualTo(height));
      Assert.That(result.PixelData, Is.EqualTo(pixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DicomReader.FromStream(null!));
  }
}
