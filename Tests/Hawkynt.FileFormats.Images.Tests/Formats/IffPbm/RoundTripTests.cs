using System;
using System.IO;
using FileFormat.IffPbm;
using FileFormat.Core;

namespace FileFormat.IffPbm.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Uncompressed() {
    var original = _CreateTestFile(16, 8, IffPbmCompression.None);

    var bytes = IffPbmWriter.ToBytes(original);
    var restored = IffPbmReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Compression, Is.EqualTo(IffPbmCompression.None));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ByteRun1Compressed() {
    var original = _CreateTestFile(16, 8, IffPbmCompression.ByteRun1);

    var bytes = IffPbmWriter.ToBytes(original);
    var restored = IffPbmReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Compression, Is.EqualTo(IffPbmCompression.ByteRun1));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PalettePreserved() {
    var palette = new byte[256 * 3];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)(i * 16 % 256);
      palette[i * 3 + 1] = (byte)(255 - i);
      palette[i * 3 + 2] = (byte)(i * 8 % 256);
    }

    var pixelData = new byte[8 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var original = new IffPbmFile {
      Width = 8,
      Height = 4,
      Compression = IffPbmCompression.None,
      PixelData = pixelData,
      Palette = palette,
      XAspect = 10,
      YAspect = 11,
      PageWidth = 320,
      PageHeight = 200,
    };

    var bytes = IffPbmWriter.ToBytes(original);
    var restored = IffPbmReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Palette, Is.Not.Null);
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      Assert.That(restored.XAspect, Is.EqualTo(10));
      Assert.That(restored.YAspect, Is.EqualTo(11));
      Assert.That(restored.PageWidth, Is.EqualTo(320));
      Assert.That(restored.PageHeight, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = _CreateTestFile(32, 16, IffPbmCompression.ByteRun1);
    var bytes = IffPbmWriter.ToBytes(original);

    var tmpPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pbm");
    try {
      File.WriteAllBytes(tmpPath, bytes);
      var restored = IffPbmReader.FromFile(new FileInfo(tmpPath));

      Assert.Multiple(() => {
        Assert.That(restored.Width, Is.EqualTo(original.Width));
        Assert.That(restored.Height, Is.EqualTo(original.Height));
        Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      });
    } finally {
      if (File.Exists(tmpPath))
        File.Delete(tmpPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var palette = new byte[16 * 3];
    for (var i = 0; i < 16; ++i) {
      palette[i * 3] = (byte)(i * 17);
      palette[i * 3 + 1] = (byte)(i * 31 % 256);
      palette[i * 3 + 2] = (byte)(i * 53 % 256);
    }

    var pixelData = new byte[8 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 16);

    var rawImage = new RawImage {
      Width = 8,
      Height = 4,
      Format = PixelFormat.Indexed8,
      PixelData = pixelData,
      Palette = palette,
      PaletteCount = 16,
    };

    var pbmFile = IffPbmFile.FromRawImage(rawImage);
    var raw2 = IffPbmFile.ToRawImage(pbmFile);

    Assert.Multiple(() => {
      Assert.That(raw2.Width, Is.EqualTo(8));
      Assert.That(raw2.Height, Is.EqualTo(4));
      Assert.That(raw2.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(raw2.PixelData, Is.EqualTo(pixelData));
      Assert.That(raw2.Palette, Is.EqualTo(palette));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new IffPbmFile {
      Width = 8,
      Height = 4,
      Compression = IffPbmCompression.ByteRun1,
      PixelData = new byte[8 * 4],
      Palette = new byte[256 * 3],
      XAspect = 1,
      YAspect = 1,
      PageWidth = 8,
      PageHeight = 4,
    };

    var bytes = IffPbmWriter.ToBytes(original);
    var restored = IffPbmReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_OddWidth() {
    var original = _CreateTestFile(7, 4, IffPbmCompression.None);

    var bytes = IffPbmWriter.ToBytes(original);
    var restored = IffPbmReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(7));
      Assert.That(restored.Height, Is.EqualTo(4));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_OddWidth_ByteRun1() {
    var original = _CreateTestFile(13, 5, IffPbmCompression.ByteRun1);

    var bytes = IffPbmWriter.ToBytes(original);
    var restored = IffPbmReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(13));
      Assert.That(restored.Height, Is.EqualTo(5));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargerImage() {
    var original = _CreateTestFile(320, 200, IffPbmCompression.ByteRun1);

    var bytes = IffPbmWriter.ToBytes(original);
    var restored = IffPbmReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(320));
      Assert.That(restored.Height, Is.EqualTo(200));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  private static IffPbmFile _CreateTestFile(int width, int height, IffPbmCompression compression) {
    var palette = new byte[256 * 3];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)(i * 17 % 256);
      palette[i * 3 + 1] = (byte)(i * 31 % 256);
      palette[i * 3 + 2] = (byte)(i * 53 % 256);
    }

    var pixelData = new byte[width * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    return new IffPbmFile {
      Width = width,
      Height = height,
      Compression = compression,
      TransparentColor = 0,
      XAspect = 10,
      YAspect = 11,
      PageWidth = width,
      PageHeight = height,
      PixelData = pixelData,
      Palette = palette,
    };
  }
}
