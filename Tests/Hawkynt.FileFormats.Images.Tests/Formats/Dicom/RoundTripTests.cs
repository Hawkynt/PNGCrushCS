using System;
using System.IO;
using FileFormat.Dicom;

namespace FileFormat.Dicom.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Mono8() {
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
    var restored = DicomReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(width));
      Assert.That(restored.Height, Is.EqualTo(height));
      Assert.That(restored.BitsAllocated, Is.EqualTo(8));
      Assert.That(restored.BitsStored, Is.EqualTo(8));
      Assert.That(restored.SamplesPerPixel, Is.EqualTo(1));
      Assert.That(restored.PhotometricInterpretation, Is.EqualTo(DicomPhotometricInterpretation.Monochrome2));
      Assert.That(restored.PixelData, Is.EqualTo(pixelData));
      Assert.That(restored.WindowCenter, Is.EqualTo(128).Within(0.001));
      Assert.That(restored.WindowWidth, Is.EqualTo(256).Within(0.001));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Mono16() {
    var width = 8;
    var height = 4;
    var pixelData = new byte[width * height * 2];
    var rng = new Random(42);
    rng.NextBytes(pixelData);

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
    var restored = DicomReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(width));
      Assert.That(restored.Height, Is.EqualTo(height));
      Assert.That(restored.BitsAllocated, Is.EqualTo(16));
      Assert.That(restored.BitsStored, Is.EqualTo(12));
      Assert.That(restored.PixelData, Is.EqualTo(pixelData));
      Assert.That(restored.WindowCenter, Is.EqualTo(2048).Within(0.001));
      Assert.That(restored.WindowWidth, Is.EqualTo(4096).Within(0.001));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb() {
    var width = 4;
    var height = 4;
    var pixelData = new byte[width * height * 3];
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
    var restored = DicomReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(width));
      Assert.That(restored.Height, Is.EqualTo(height));
      Assert.That(restored.BitsAllocated, Is.EqualTo(8));
      Assert.That(restored.SamplesPerPixel, Is.EqualTo(3));
      Assert.That(restored.PhotometricInterpretation, Is.EqualTo(DicomPhotometricInterpretation.Rgb));
      Assert.That(restored.PixelData, Is.EqualTo(pixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var width = 4;
    var height = 3;
    var pixelData = new byte[width * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new DicomFile {
      Width = width,
      Height = height,
      BitsAllocated = 8,
      BitsStored = 8,
      SamplesPerPixel = 1,
      PhotometricInterpretation = DicomPhotometricInterpretation.Monochrome2,
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dcm");
    try {
      var bytes = DicomWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = DicomReader.FromFile(new FileInfo(tempPath));

      Assert.Multiple(() => {
        Assert.That(restored.Width, Is.EqualTo(width));
        Assert.That(restored.Height, Is.EqualTo(height));
        Assert.That(restored.PixelData, Is.EqualTo(pixelData));
      });
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
