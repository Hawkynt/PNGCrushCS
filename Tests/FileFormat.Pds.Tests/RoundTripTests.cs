using System;
using System.IO;
using FileFormat.Pds;

namespace FileFormat.Pds.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale8Bit() {
    var width = 4;
    var height = 3;
    var pixelData = new byte[width * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var original = new PdsFile {
      Width = width,
      Height = height,
      SampleBits = 8,
      Bands = 1,
      SampleType = PdsSampleType.UnsignedByte,
      BandStorage = PdsBandStorage.BandSequential,
      PixelData = pixelData
    };

    var bytes = PdsWriter.ToBytes(original);
    var restored = PdsReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Bands, Is.EqualTo(original.Bands));
    Assert.That(restored.SampleBits, Is.EqualTo(original.SampleBits));
    Assert.That(restored.SampleType, Is.EqualTo(original.SampleType));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb8BitBip() {
    var width = 4;
    var height = 3;
    var bands = 3;
    var pixelData = new byte[width * height * bands];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new PdsFile {
      Width = width,
      Height = height,
      SampleBits = 8,
      Bands = bands,
      SampleType = PdsSampleType.UnsignedByte,
      BandStorage = PdsBandStorage.SampleInterleaved,
      PixelData = pixelData
    };

    var bytes = PdsWriter.ToBytes(original);
    var restored = PdsReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Bands, Is.EqualTo(bands));
    Assert.That(restored.BandStorage, Is.EqualTo(PdsBandStorage.SampleInterleaved));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb8BitBsq() {
    var width = 3;
    var height = 2;
    var bands = 3;
    var pixelData = new byte[width * height * bands];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new PdsFile {
      Width = width,
      Height = height,
      SampleBits = 8,
      Bands = bands,
      SampleType = PdsSampleType.UnsignedByte,
      BandStorage = PdsBandStorage.BandSequential,
      PixelData = pixelData
    };

    var bytes = PdsWriter.ToBytes(original);
    var restored = PdsReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Bands, Is.EqualTo(bands));
    Assert.That(restored.BandStorage, Is.EqualTo(PdsBandStorage.BandSequential));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pds");
    try {
      var original = new PdsFile {
        Width = 3,
        Height = 2,
        SampleBits = 8,
        Bands = 1,
        SampleType = PdsSampleType.UnsignedByte,
        BandStorage = PdsBandStorage.BandSequential,
        PixelData = [0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE]
      };

      var bytes = PdsWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = PdsReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_Grayscale() {
    var width = 4;
    var height = 2;
    var pixelData = new byte[width * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 31 % 256);

    var file = new PdsFile {
      Width = width,
      Height = height,
      SampleBits = 8,
      Bands = 1,
      SampleType = PdsSampleType.UnsignedByte,
      BandStorage = PdsBandStorage.BandSequential,
      PixelData = pixelData
    };

    var raw = PdsFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(width));
    Assert.That(raw.Height, Is.EqualTo(height));
    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Gray8));
    Assert.That(raw.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_RgbBsq() {
    var width = 2;
    var height = 2;
    var pixelCount = width * height;
    // BSQ: all R, then all G, then all B
    var pixelData = new byte[pixelCount * 3];
    for (var i = 0; i < pixelCount; ++i) {
      pixelData[i] = (byte)(i * 10);                    // R band
      pixelData[pixelCount + i] = (byte)(i * 20);       // G band
      pixelData[pixelCount * 2 + i] = (byte)(i * 30);   // B band
    }

    var file = new PdsFile {
      Width = width,
      Height = height,
      SampleBits = 8,
      Bands = 3,
      SampleType = PdsSampleType.UnsignedByte,
      BandStorage = PdsBandStorage.BandSequential,
      PixelData = pixelData
    };

    var raw = PdsFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Rgb24));
    // verify deinterleaving: pixel 0 = (R0, G0, B0)
    Assert.That(raw.PixelData[0], Is.EqualTo(0));    // R
    Assert.That(raw.PixelData[1], Is.EqualTo(0));    // G
    Assert.That(raw.PixelData[2], Is.EqualTo(0));    // B
    // pixel 1 = (R1, G1, B1)
    Assert.That(raw.PixelData[3], Is.EqualTo(10));   // R
    Assert.That(raw.PixelData[4], Is.EqualTo(20));   // G
    Assert.That(raw.PixelData[5], Is.EqualTo(30));   // B
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_RgbBip() {
    var width = 2;
    var height = 1;
    // BIP: R,G,B,R,G,B
    var pixelData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF };

    var file = new PdsFile {
      Width = width,
      Height = height,
      SampleBits = 8,
      Bands = 3,
      SampleType = PdsSampleType.UnsignedByte,
      BandStorage = PdsBandStorage.SampleInterleaved,
      PixelData = pixelData
    };

    var raw = PdsFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Rgb24));
    Assert.That(raw.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_FromRawImage_Grayscale() {
    var raw = new FileFormat.Core.RawImage {
      Width = 4,
      Height = 2,
      Format = FileFormat.Core.PixelFormat.Gray8,
      PixelData = new byte[] { 10, 20, 30, 40, 50, 60, 70, 80 },
    };

    var pds = PdsFile.FromRawImage(raw);

    Assert.That(pds.Width, Is.EqualTo(4));
    Assert.That(pds.Height, Is.EqualTo(2));
    Assert.That(pds.Bands, Is.EqualTo(1));
    Assert.That(pds.SampleBits, Is.EqualTo(8));
    Assert.That(pds.PixelData, Is.EqualTo(raw.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_FromRawImage_Rgb() {
    var raw = new FileFormat.Core.RawImage {
      Width = 2,
      Height = 1,
      Format = FileFormat.Core.PixelFormat.Rgb24,
      PixelData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF },
    };

    var pds = PdsFile.FromRawImage(raw);

    Assert.That(pds.Width, Is.EqualTo(2));
    Assert.That(pds.Height, Is.EqualTo(1));
    Assert.That(pds.Bands, Is.EqualTo(3));
    Assert.That(pds.BandStorage, Is.EqualTo(PdsBandStorage.SampleInterleaved));
    Assert.That(pds.PixelData, Is.EqualTo(raw.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_16BitGrayscale() {
    var width = 2;
    var height = 1;
    // MSB 16-bit: pixel 0 = 0xFF00, pixel 1 = 0x8000
    var pixelData = new byte[] { 0xFF, 0x00, 0x80, 0x00 };

    var file = new PdsFile {
      Width = width,
      Height = height,
      SampleBits = 16,
      Bands = 1,
      SampleType = PdsSampleType.MsbUnsigned16,
      BandStorage = PdsBandStorage.BandSequential,
      PixelData = pixelData
    };

    var raw = PdsFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Gray16));
    // Gray16 is big-endian uint16: pixel 0 = 0xFF00, pixel 1 = 0x8000
    Assert.That(raw.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(raw.PixelData[1], Is.EqualTo(0x00));
    Assert.That(raw.PixelData[2], Is.EqualTo(0x80));
    Assert.That(raw.PixelData[3], Is.EqualTo(0x00));
  }
}
