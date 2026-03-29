using System;
using System.IO;
using FileFormat.Core;
using FileFormat.DigiView;

namespace FileFormat.DigiView.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale_WriteRead_AllFieldsPreserved() {
    var pixelData = new byte[256];
    for (var i = 0; i < 256; ++i)
      pixelData[i] = (byte)i;

    var original = new DigiViewFile {
      Width = 16,
      Height = 16,
      Channels = 1,
      PixelData = pixelData,
    };

    var bytes = DigiViewWriter.ToBytes(original);
    var restored = DigiViewReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Channels, Is.EqualTo(original.Channels));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb_WriteRead_AllFieldsPreserved() {
    var pixelData = new byte[192];
    for (var i = 0; i < 192; ++i)
      pixelData[i] = (byte)(i * 3 % 256);

    var original = new DigiViewFile {
      Width = 8,
      Height = 8,
      Channels = 3,
      PixelData = pixelData,
    };

    var bytes = DigiViewWriter.ToBytes(original);
    var restored = DigiViewReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Channels, Is.EqualTo(original.Channels));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros_Grayscale() {
    var original = new DigiViewFile {
      Width = 4,
      Height = 4,
      Channels = 1,
      PixelData = new byte[16],
    };

    var bytes = DigiViewWriter.ToBytes(original);
    var restored = DigiViewReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.Channels, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros_Rgb() {
    var original = new DigiViewFile {
      Width = 4,
      Height = 4,
      Channels = 3,
      PixelData = new byte[48],
    };

    var bytes = DigiViewWriter.ToBytes(original);
    var restored = DigiViewReader.FromBytes(bytes);

    Assert.That(restored.Channels, Is.EqualTo(3));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_Grayscale() {
    var pixelData = new byte[64];
    for (var i = 0; i < 64; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new DigiViewFile {
      Width = 8,
      Height = 8,
      Channels = 1,
      PixelData = pixelData,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dgv");
    try {
      var bytes = DigiViewWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = DigiViewReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Channels, Is.EqualTo(original.Channels));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_Rgb() {
    var pixelData = new byte[48];
    for (var i = 0; i < 48; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new DigiViewFile {
      Width = 4,
      Height = 4,
      Channels = 3,
      PixelData = pixelData,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dgv");
    try {
      var bytes = DigiViewWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = DigiViewReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Channels, Is.EqualTo(original.Channels));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Gray8() {
    var pixelData = new byte[64];
    for (var i = 0; i < 64; ++i)
      pixelData[i] = (byte)(i * 3 % 256);

    var original = new DigiViewFile {
      Width = 8,
      Height = 8,
      Channels = 1,
      PixelData = pixelData,
    };

    var raw = DigiViewFile.ToRawImage(original);
    var restored = DigiViewFile.FromRawImage(raw);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Channels, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Rgb24() {
    var pixelData = new byte[48];
    for (var i = 0; i < 48; ++i)
      pixelData[i] = (byte)(i * 5 % 256);

    var original = new DigiViewFile {
      Width = 4,
      Height = 4,
      Channels = 3,
      PixelData = pixelData,
    };

    var raw = DigiViewFile.ToRawImage(original);
    var restored = DigiViewFile.FromRawImage(raw);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Channels, Is.EqualTo(3));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_AllZeros_Gray() {
    var original = new DigiViewFile {
      Width = 4,
      Height = 4,
      Channels = 1,
      PixelData = new byte[16],
    };

    var raw = DigiViewFile.ToRawImage(original);
    var restored = DigiViewFile.FromRawImage(raw);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaStream_Rgb() {
    var pixelData = new byte[12];
    for (var i = 0; i < 12; ++i)
      pixelData[i] = (byte)(0xAA + i);

    var original = new DigiViewFile {
      Width = 2,
      Height = 2,
      Channels = 3,
      PixelData = pixelData,
    };

    var bytes = DigiViewWriter.ToBytes(original);

    using var ms = new MemoryStream(bytes);
    var restored = DigiViewReader.FromStream(ms);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Channels, Is.EqualTo(3));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargeImage_Rgb() {
    var pixelData = new byte[320 * 200 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new DigiViewFile {
      Width = 320,
      Height = 200,
      Channels = 3,
      PixelData = pixelData,
    };

    var bytes = DigiViewWriter.ToBytes(original);
    var restored = DigiViewReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(320));
    Assert.That(restored.Height, Is.EqualTo(200));
    Assert.That(restored.Channels, Is.EqualTo(3));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
