using System;
using System.IO;
using FileFormat.Blp;

namespace FileFormat.Blp.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_UncompressedBgra_DataPreserved() {
    var pixelData = new byte[4 * 4 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new BlpFile {
      Width = 4,
      Height = 4,
      Encoding = BlpEncoding.UncompressedBgra,
      AlphaDepth = 8,
      HasMips = false,
      MipData = [pixelData],
    };

    var bytes = BlpWriter.ToBytes(original);
    var restored = BlpReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.Encoding, Is.EqualTo(BlpEncoding.UncompressedBgra));
    Assert.That(restored.MipData, Has.Length.EqualTo(1));
    Assert.That(restored.MipData[0], Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PaletteNoAlpha_DataPreserved() {
    var palette = new byte[1024];
    for (var i = 0; i < 256; ++i) {
      palette[i * 4] = (byte)(i * 3 % 256);     // B
      palette[i * 4 + 1] = (byte)(i * 5 % 256); // G
      palette[i * 4 + 2] = (byte)(i * 7 % 256); // R
      palette[i * 4 + 3] = 255;                  // A
    }

    var pixelIndices = new byte[8 * 8];
    for (var i = 0; i < pixelIndices.Length; ++i)
      pixelIndices[i] = (byte)(i % 256);

    var original = new BlpFile {
      Width = 8,
      Height = 8,
      Encoding = BlpEncoding.Palette,
      AlphaDepth = 0,
      HasMips = false,
      Palette = palette,
      MipData = [pixelIndices],
    };

    var bytes = BlpWriter.ToBytes(original);
    var restored = BlpReader.FromBytes(bytes);

    Assert.That(restored.Encoding, Is.EqualTo(BlpEncoding.Palette));
    Assert.That(restored.Palette, Is.EqualTo(palette));
    Assert.That(restored.MipData[0], Is.EqualTo(pixelIndices));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PaletteWithAlpha8_DataPreserved() {
    var palette = new byte[1024];
    for (var i = 0; i < 256; ++i) {
      palette[i * 4] = (byte)i;
      palette[i * 4 + 1] = (byte)i;
      palette[i * 4 + 2] = (byte)i;
      palette[i * 4 + 3] = 255;
    }

    var totalPixels = 4 * 4;
    var pixelData = new byte[totalPixels + totalPixels]; // indices + alpha bytes
    for (var i = 0; i < totalPixels; ++i) {
      pixelData[i] = (byte)(i % 256);
      pixelData[totalPixels + i] = (byte)(255 - i);
    }

    var original = new BlpFile {
      Width = 4,
      Height = 4,
      Encoding = BlpEncoding.Palette,
      AlphaDepth = 8,
      HasMips = false,
      Palette = palette,
      MipData = [pixelData],
    };

    var bytes = BlpWriter.ToBytes(original);
    var restored = BlpReader.FromBytes(bytes);

    Assert.That(restored.AlphaDepth, Is.EqualTo(8));
    Assert.That(restored.MipData[0], Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_DataPreserved() {
    var pixelData = new byte[4 * 4 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new BlpFile {
      Width = 4,
      Height = 4,
      Encoding = BlpEncoding.UncompressedBgra,
      AlphaDepth = 8,
      HasMips = false,
      MipData = [pixelData],
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".blp");
    try {
      var bytes = BlpWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);
      var restored = BlpReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(4));
      Assert.That(restored.Height, Is.EqualTo(4));
      Assert.That(restored.MipData[0], Is.EqualTo(pixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BgraToRawImage_PixelDataPreserved() {
    var pixelData = new byte[4 * 4 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var file = new BlpFile {
      Width = 4,
      Height = 4,
      Encoding = BlpEncoding.UncompressedBgra,
      AlphaDepth = 8,
      HasMips = false,
      MipData = [pixelData],
    };

    var raw = BlpFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(4));
    Assert.That(raw.Height, Is.EqualTo(4));
    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Bgra32));
    Assert.That(raw.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PaletteToRawImage_PixelDataDecoded() {
    var palette = new byte[1024];
    // Entry 0 = blue (BGRA)
    palette[0] = 255; palette[1] = 0; palette[2] = 0; palette[3] = 255;
    // Entry 1 = green (BGRA)
    palette[4] = 0; palette[5] = 255; palette[6] = 0; palette[7] = 255;

    var indices = new byte[4]; // 2x2 = 4 pixels
    indices[0] = 0; // blue
    indices[1] = 1; // green
    indices[2] = 1; // green
    indices[3] = 0; // blue

    var file = new BlpFile {
      Width = 2,
      Height = 2,
      Encoding = BlpEncoding.Palette,
      AlphaDepth = 0,
      HasMips = false,
      Palette = palette,
      MipData = [indices],
    };

    var raw = BlpFile.ToRawImage(file);

    // Pixel 0: palette entry 0 = B=255, G=0, R=0
    Assert.That(raw.PixelData[0], Is.EqualTo(255)); // B
    Assert.That(raw.PixelData[1], Is.EqualTo(0));   // G
    Assert.That(raw.PixelData[2], Is.EqualTo(0));   // R
    Assert.That(raw.PixelData[3], Is.EqualTo(255)); // A (no alpha = opaque)

    // Pixel 1: palette entry 1 = B=0, G=255, R=0
    Assert.That(raw.PixelData[4], Is.EqualTo(0));   // B
    Assert.That(raw.PixelData[5], Is.EqualTo(255)); // G
    Assert.That(raw.PixelData[6], Is.EqualTo(0));   // R
    Assert.That(raw.PixelData[7], Is.EqualTo(255)); // A
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_FromRawImage_ProducesValidBlp() {
    var raw = new FileFormat.Core.RawImage {
      Width = 4,
      Height = 4,
      Format = FileFormat.Core.PixelFormat.Bgra32,
      PixelData = new byte[4 * 4 * 4],
    };

    var blpFile = BlpFile.FromRawImage(raw);

    Assert.That(blpFile.Width, Is.EqualTo(4));
    Assert.That(blpFile.Height, Is.EqualTo(4));
    Assert.That(blpFile.Encoding, Is.EqualTo(BlpEncoding.UncompressedBgra));
    Assert.That(blpFile.MipData, Has.Length.EqualTo(1));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultipleMips_AllPreserved() {
    var mip0 = new byte[8 * 8 * 4]; // 8x8
    var mip1 = new byte[4 * 4 * 4]; // 4x4
    var mip2 = new byte[2 * 2 * 4]; // 2x2

    for (var i = 0; i < mip0.Length; ++i)
      mip0[i] = (byte)(i % 256);
    for (var i = 0; i < mip1.Length; ++i)
      mip1[i] = (byte)(i * 3 % 256);
    for (var i = 0; i < mip2.Length; ++i)
      mip2[i] = (byte)(i * 7 % 256);

    var original = new BlpFile {
      Width = 8,
      Height = 8,
      Encoding = BlpEncoding.UncompressedBgra,
      AlphaDepth = 8,
      HasMips = true,
      MipData = [mip0, mip1, mip2],
    };

    var bytes = BlpWriter.ToBytes(original);
    var restored = BlpReader.FromBytes(bytes);

    Assert.That(restored.HasMips, Is.True);
    Assert.That(restored.MipData, Has.Length.EqualTo(3));
    Assert.That(restored.MipData[0], Is.EqualTo(mip0));
    Assert.That(restored.MipData[1], Is.EqualTo(mip1));
    Assert.That(restored.MipData[2], Is.EqualTo(mip2));
  }
}
