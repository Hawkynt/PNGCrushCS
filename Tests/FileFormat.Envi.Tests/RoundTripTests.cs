using System;
using System.IO;
using FileFormat.Envi;

namespace FileFormat.Envi.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale() {
    var width = 4;
    var height = 3;
    var pixelData = new byte[width * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var original = new EnviFile {
      Width = width,
      Height = height,
      Bands = 1,
      DataType = 1,
      Interleave = EnviInterleave.Bsq,
      PixelData = pixelData
    };

    var bytes = EnviWriter.ToBytes(original);
    var restored = EnviReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Bands, Is.EqualTo(original.Bands));
    Assert.That(restored.DataType, Is.EqualTo(original.DataType));
    Assert.That(restored.Interleave, Is.EqualTo(original.Interleave));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_RgbBip() {
    var width = 4;
    var height = 3;
    var bands = 3;
    var pixelData = new byte[width * height * bands];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new EnviFile {
      Width = width,
      Height = height,
      Bands = bands,
      DataType = 1,
      Interleave = EnviInterleave.Bip,
      PixelData = pixelData
    };

    var bytes = EnviWriter.ToBytes(original);
    var restored = EnviReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Bands, Is.EqualTo(bands));
    Assert.That(restored.Interleave, Is.EqualTo(EnviInterleave.Bip));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_RgbBsq() {
    var width = 3;
    var height = 2;
    var bands = 3;
    var pixelData = new byte[width * height * bands];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new EnviFile {
      Width = width,
      Height = height,
      Bands = bands,
      DataType = 1,
      Interleave = EnviInterleave.Bsq,
      PixelData = pixelData
    };

    var bytes = EnviWriter.ToBytes(original);
    var restored = EnviReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Bands, Is.EqualTo(bands));
    Assert.That(restored.Interleave, Is.EqualTo(EnviInterleave.Bsq));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_RgbBil() {
    var width = 3;
    var height = 2;
    var bands = 3;
    var pixelData = new byte[width * height * bands];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new EnviFile {
      Width = width,
      Height = height,
      Bands = bands,
      DataType = 1,
      Interleave = EnviInterleave.Bil,
      PixelData = pixelData
    };

    var bytes = EnviWriter.ToBytes(original);
    var restored = EnviReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Interleave, Is.EqualTo(EnviInterleave.Bil));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hdr");
    try {
      var original = new EnviFile {
        Width = 3,
        Height = 2,
        Bands = 1,
        DataType = 1,
        Interleave = EnviInterleave.Bsq,
        PixelData = [0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE]
      };

      var bytes = EnviWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = EnviReader.FromFile(new FileInfo(tempPath));

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

    var file = new EnviFile {
      Width = width,
      Height = height,
      Bands = 1,
      DataType = 1,
      Interleave = EnviInterleave.Bsq,
      PixelData = pixelData
    };

    var raw = EnviFile.ToRawImage(file);

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
      pixelData[i] = (byte)(i * 10);
      pixelData[pixelCount + i] = (byte)(i * 20);
      pixelData[pixelCount * 2 + i] = (byte)(i * 30);
    }

    var file = new EnviFile {
      Width = width,
      Height = height,
      Bands = 3,
      DataType = 1,
      Interleave = EnviInterleave.Bsq,
      PixelData = pixelData
    };

    var raw = EnviFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Rgb24));
    Assert.That(raw.PixelData[0], Is.EqualTo(0));
    Assert.That(raw.PixelData[1], Is.EqualTo(0));
    Assert.That(raw.PixelData[2], Is.EqualTo(0));
    Assert.That(raw.PixelData[3], Is.EqualTo(10));
    Assert.That(raw.PixelData[4], Is.EqualTo(20));
    Assert.That(raw.PixelData[5], Is.EqualTo(30));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_RgbBip() {
    var width = 2;
    var height = 1;
    // BIP: R,G,B,R,G,B
    var pixelData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF };

    var file = new EnviFile {
      Width = width,
      Height = height,
      Bands = 3,
      DataType = 1,
      Interleave = EnviInterleave.Bip,
      PixelData = pixelData
    };

    var raw = EnviFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Rgb24));
    Assert.That(raw.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_RgbBil() {
    var width = 2;
    var height = 2;
    // BIL: for each line, all samples of band0, then band1, then band2
    // line 0: R0,R1, G0,G1, B0,B1
    // line 1: R2,R3, G2,G3, B2,B3
    var pixelData = new byte[] {
      10, 20, 30, 40, 50, 60,  // line 0: R0=10,R1=20, G0=30,G1=40, B0=50,B1=60
      70, 80, 90, 100, 110, 120 // line 1: R2=70,R3=80, G2=90,G3=100, B2=110,B3=120
    };

    var file = new EnviFile {
      Width = width,
      Height = height,
      Bands = 3,
      DataType = 1,
      Interleave = EnviInterleave.Bil,
      PixelData = pixelData
    };

    var raw = EnviFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Rgb24));
    // pixel (0,0) = R10, G30, B50
    Assert.That(raw.PixelData[0], Is.EqualTo(10));
    Assert.That(raw.PixelData[1], Is.EqualTo(30));
    Assert.That(raw.PixelData[2], Is.EqualTo(50));
    // pixel (1,0) = R20, G40, B60
    Assert.That(raw.PixelData[3], Is.EqualTo(20));
    Assert.That(raw.PixelData[4], Is.EqualTo(40));
    Assert.That(raw.PixelData[5], Is.EqualTo(60));
    // pixel (0,1) = R70, G90, B110
    Assert.That(raw.PixelData[6], Is.EqualTo(70));
    Assert.That(raw.PixelData[7], Is.EqualTo(90));
    Assert.That(raw.PixelData[8], Is.EqualTo(110));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_FromRawImage_Grayscale() {
    var raw = new FileFormat.Core.RawImage {
      Width = 4,
      Height = 2,
      Format = FileFormat.Core.PixelFormat.Gray8,
      PixelData = [10, 20, 30, 40, 50, 60, 70, 80],
    };

    var envi = EnviFile.FromRawImage(raw);

    Assert.That(envi.Width, Is.EqualTo(4));
    Assert.That(envi.Height, Is.EqualTo(2));
    Assert.That(envi.Bands, Is.EqualTo(1));
    Assert.That(envi.DataType, Is.EqualTo(1));
    Assert.That(envi.PixelData, Is.EqualTo(raw.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_FromRawImage_Rgb() {
    var raw = new FileFormat.Core.RawImage {
      Width = 2,
      Height = 1,
      Format = FileFormat.Core.PixelFormat.Rgb24,
      PixelData = [0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF],
    };

    var envi = EnviFile.FromRawImage(raw);

    Assert.That(envi.Width, Is.EqualTo(2));
    Assert.That(envi.Height, Is.EqualTo(1));
    Assert.That(envi.Bands, Is.EqualTo(3));
    Assert.That(envi.Interleave, Is.EqualTo(EnviInterleave.Bip));
    Assert.That(envi.PixelData, Is.EqualTo(raw.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Grayscale() {
    var raw = new FileFormat.Core.RawImage {
      Width = 3,
      Height = 2,
      Format = FileFormat.Core.PixelFormat.Gray8,
      PixelData = [10, 20, 30, 40, 50, 60],
    };

    var envi = EnviFile.FromRawImage(raw);
    var bytes = EnviWriter.ToBytes(envi);
    var restored = EnviReader.FromBytes(bytes);
    var rawBack = EnviFile.ToRawImage(restored);

    Assert.That(rawBack.Width, Is.EqualTo(raw.Width));
    Assert.That(rawBack.Height, Is.EqualTo(raw.Height));
    Assert.That(rawBack.Format, Is.EqualTo(raw.Format));
    Assert.That(rawBack.PixelData, Is.EqualTo(raw.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Rgb() {
    var raw = new FileFormat.Core.RawImage {
      Width = 2,
      Height = 2,
      Format = FileFormat.Core.PixelFormat.Rgb24,
      PixelData = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120],
    };

    var envi = EnviFile.FromRawImage(raw);
    var bytes = EnviWriter.ToBytes(envi);
    var restored = EnviReader.FromBytes(bytes);
    var rawBack = EnviFile.ToRawImage(restored);

    Assert.That(rawBack.Width, Is.EqualTo(raw.Width));
    Assert.That(rawBack.Height, Is.EqualTo(raw.Height));
    Assert.That(rawBack.Format, Is.EqualTo(raw.Format));
    Assert.That(rawBack.PixelData, Is.EqualTo(raw.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var width = 4;
    var height = 3;
    var pixelData = new byte[width * height];

    var original = new EnviFile {
      Width = width,
      Height = height,
      Bands = 1,
      DataType = 1,
      Interleave = EnviInterleave.Bsq,
      PixelData = pixelData
    };

    var bytes = EnviWriter.ToBytes(original);
    var restored = EnviReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
