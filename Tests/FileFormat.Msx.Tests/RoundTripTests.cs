using System;
using System.IO;
using FileFormat.Msx;

namespace FileFormat.Msx.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Sc2_PreservesData() {
    var pixelData = new byte[16384];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var original = new MsxFile {
      Width = 256,
      Height = 192,
      Mode = MsxMode.Screen2,
      BitsPerPixel = 1,
      PixelData = pixelData,
      HasBloadHeader = false
    };

    var bytes = MsxWriter.ToBytes(original);
    var restored = MsxReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(256));
    Assert.That(restored.Height, Is.EqualTo(192));
    Assert.That(restored.Mode, Is.EqualTo(MsxMode.Screen2));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(1));
    Assert.That(restored.Palette, Is.Null);
    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Sc5_PreservesData() {
    var pixelData = new byte[26848];
    var palette = new byte[32];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 3 % 256);
    for (var i = 0; i < palette.Length; ++i)
      palette[i] = (byte)(i * 7 % 256);

    var original = new MsxFile {
      Width = 256,
      Height = 212,
      Mode = MsxMode.Screen5,
      BitsPerPixel = 4,
      PixelData = pixelData,
      Palette = palette,
      HasBloadHeader = false
    };

    var bytes = MsxWriter.ToBytes(original);
    var restored = MsxReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(256));
    Assert.That(restored.Height, Is.EqualTo(212));
    Assert.That(restored.Mode, Is.EqualTo(MsxMode.Screen5));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(4));
    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
    Assert.That(restored.Palette, Is.EqualTo(palette));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Sc8_PreservesData() {
    var pixelData = new byte[54272];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new MsxFile {
      Width = 256,
      Height = 212,
      Mode = MsxMode.Screen8,
      BitsPerPixel = 8,
      PixelData = pixelData,
      HasBloadHeader = false
    };

    var bytes = MsxWriter.ToBytes(original);
    var restored = MsxReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(256));
    Assert.That(restored.Height, Is.EqualTo(212));
    Assert.That(restored.Mode, Is.EqualTo(MsxMode.Screen8));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(8));
    Assert.That(restored.Palette, Is.Null);
    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithBload_PreservesHeader() {
    var pixelData = new byte[16384];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var original = new MsxFile {
      Width = 256,
      Height = 192,
      Mode = MsxMode.Screen2,
      BitsPerPixel = 1,
      PixelData = pixelData,
      HasBloadHeader = true
    };

    var bytes = MsxWriter.ToBytes(original);
    var restored = MsxReader.FromBytes(bytes);

    Assert.That(restored.HasBloadHeader, Is.True);
    Assert.That(restored.Mode, Is.EqualTo(MsxMode.Screen2));
    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithoutBload_NoBloadHeader() {
    var pixelData = new byte[54272];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new MsxFile {
      Width = 256,
      Height = 212,
      Mode = MsxMode.Screen8,
      BitsPerPixel = 8,
      PixelData = pixelData,
      HasBloadHeader = false
    };

    var bytes = MsxWriter.ToBytes(original);
    var restored = MsxReader.FromBytes(bytes);

    Assert.That(restored.HasBloadHeader, Is.False);
    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_PreservesData() {
    var pixelData = new byte[16384];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new MsxFile {
      Width = 256,
      Height = 192,
      Mode = MsxMode.Screen2,
      BitsPerPixel = 1,
      PixelData = pixelData,
      HasBloadHeader = false
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sc2");
    try {
      var bytes = MsxWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);
      var restored = MsxReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(256));
      Assert.That(restored.Height, Is.EqualTo(192));
      Assert.That(restored.PixelData, Is.EqualTo(pixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
