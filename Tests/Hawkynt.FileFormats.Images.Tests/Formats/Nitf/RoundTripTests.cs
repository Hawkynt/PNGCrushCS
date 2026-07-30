using System;
using System.IO;
using FileFormat.Nitf;
using FileFormat.Core;

namespace FileFormat.Nitf.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale() {
    var pixelData = new byte[8 * 6];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 5 % 256);

    var original = new NitfFile {
      Width = 8,
      Height = 6,
      Mode = NitfImageMode.Grayscale,
      PixelData = pixelData,
    };

    var bytes = NitfWriter.ToBytes(original);
    var restored = NitfReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Mode, Is.EqualTo(NitfImageMode.Grayscale));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb() {
    var pixelData = new byte[4 * 3 * 3]; // 4x3 RGB, band-sequential
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new NitfFile {
      Width = 4,
      Height = 3,
      Mode = NitfImageMode.Rgb,
      PixelData = pixelData,
    };

    var bytes = NitfWriter.ToBytes(original);
    var restored = NitfReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Mode, Is.EqualTo(NitfImageMode.Rgb));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[6 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new NitfFile {
      Width = 6,
      Height = 4,
      Mode = NitfImageMode.Grayscale,
      PixelData = pixelData,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ntf");
    try {
      var bytes = NitfWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = NitfReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Mode, Is.EqualTo(original.Mode));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros_Grayscale() {
    var original = new NitfFile {
      Width = 4,
      Height = 4,
      Mode = NitfImageMode.Grayscale,
      PixelData = new byte[16],
    };

    var bytes = NitfWriter.ToBytes(original);
    var restored = NitfReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_TitlePreserved() {
    var original = new NitfFile {
      Width = 2,
      Height = 2,
      Mode = NitfImageMode.Grayscale,
      Title = "My Test Image",
      PixelData = new byte[4],
    };

    var bytes = NitfWriter.ToBytes(original);
    var restored = NitfReader.FromBytes(bytes);

    Assert.That(restored.Title, Is.EqualTo("My Test Image"));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_Grayscale() {
    var pixelData = new byte[4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 21 % 256);

    var file = new NitfFile {
      Width = 4,
      Height = 3,
      Mode = NitfImageMode.Grayscale,
      PixelData = pixelData,
    };

    var raw = NitfFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(4));
    Assert.That(raw.Height, Is.EqualTo(3));
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));
    Assert.That(raw.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_Rgb() {
    // Band-sequential: R-plane(4 bytes), G-plane(4 bytes), B-plane(4 bytes) for 2x2
    var pixelData = new byte[2 * 2 * 3];
    // R-plane
    pixelData[0] = 255; pixelData[1] = 128; pixelData[2] = 64; pixelData[3] = 32;
    // G-plane
    pixelData[4] = 10; pixelData[5] = 20; pixelData[6] = 30; pixelData[7] = 40;
    // B-plane
    pixelData[8] = 100; pixelData[9] = 150; pixelData[10] = 200; pixelData[11] = 250;

    var file = new NitfFile {
      Width = 2,
      Height = 2,
      Mode = NitfImageMode.Rgb,
      PixelData = pixelData,
    };

    var raw = NitfFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    // First pixel: R=255, G=10, B=100
    Assert.That(raw.PixelData[0], Is.EqualTo(255));
    Assert.That(raw.PixelData[1], Is.EqualTo(10));
    Assert.That(raw.PixelData[2], Is.EqualTo(100));
    // Second pixel: R=128, G=20, B=150
    Assert.That(raw.PixelData[3], Is.EqualTo(128));
    Assert.That(raw.PixelData[4], Is.EqualTo(20));
    Assert.That(raw.PixelData[5], Is.EqualTo(150));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_FromRawImage_Grayscale() {
    var raw = new RawImage {
      Width = 3,
      Height = 2,
      Format = PixelFormat.Gray8,
      PixelData = [10, 20, 30, 40, 50, 60],
    };

    var file = NitfFile.FromRawImage(raw);

    Assert.That(file.Width, Is.EqualTo(3));
    Assert.That(file.Height, Is.EqualTo(2));
    Assert.That(file.Mode, Is.EqualTo(NitfImageMode.Grayscale));
    Assert.That(file.PixelData, Is.EqualTo(raw.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_FromRawImage_Rgb() {
    // Interleaved: R,G,B,R,G,B,...
    var raw = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [255, 0, 100, 128, 64, 200],
    };

    var file = NitfFile.FromRawImage(raw);

    Assert.That(file.Width, Is.EqualTo(2));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.Mode, Is.EqualTo(NitfImageMode.Rgb));
    // Band-sequential: R-plane=[255,128], G-plane=[0,64], B-plane=[100,200]
    Assert.That(file.PixelData[0], Is.EqualTo(255));
    Assert.That(file.PixelData[1], Is.EqualTo(128));
    Assert.That(file.PixelData[2], Is.EqualTo(0));
    Assert.That(file.PixelData[3], Is.EqualTo(64));
    Assert.That(file.PixelData[4], Is.EqualTo(100));
    Assert.That(file.PixelData[5], Is.EqualTo(200));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_RawImage_Grayscale_FullCycle() {
    var original = new RawImage {
      Width = 4,
      Height = 3,
      Format = PixelFormat.Gray8,
      PixelData = new byte[12],
    };
    for (var i = 0; i < 12; ++i)
      original.PixelData[i] = (byte)(i * 19 % 256);

    var nitf = NitfFile.FromRawImage(original);
    var bytes = NitfWriter.ToBytes(nitf);
    var restored = NitfReader.FromBytes(bytes);
    var raw = NitfFile.ToRawImage(restored);

    Assert.That(raw.Width, Is.EqualTo(original.Width));
    Assert.That(raw.Height, Is.EqualTo(original.Height));
    Assert.That(raw.Format, Is.EqualTo(original.Format));
    Assert.That(raw.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_RawImage_Rgb_FullCycle() {
    var original = new RawImage {
      Width = 3,
      Height = 2,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[18],
    };
    for (var i = 0; i < 18; ++i)
      original.PixelData[i] = (byte)(i * 11 % 256);

    var nitf = NitfFile.FromRawImage(original);
    var bytes = NitfWriter.ToBytes(nitf);
    var restored = NitfReader.FromBytes(bytes);
    var raw = NitfFile.ToRawImage(restored);

    Assert.That(raw.Width, Is.EqualTo(original.Width));
    Assert.That(raw.Height, Is.EqualTo(original.Height));
    Assert.That(raw.Format, Is.EqualTo(original.Format));
    Assert.That(raw.PixelData, Is.EqualTo(original.PixelData));
  }
}
