using System;
using FileFormat.FullscreenKit;
using FileFormat.Core;

namespace FileFormat.FullscreenKit.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_PrimaryVariant_PreservesAllFields() {
    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = (short)(i * 0x0111 & 0x0777);

    var pixelData = new byte[FullscreenKitFile.PrimaryPixelDataSize];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new FullscreenKitFile {
      Width = FullscreenKitFile.PrimaryWidth,
      Height = FullscreenKitFile.PrimaryHeight,
      Palette = palette,
      PixelData = pixelData
    };

    var bytes = FullscreenKitWriter.ToBytes(original);
    var restored = FullscreenKitReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(416));
      Assert.That(restored.Height, Is.EqualTo(274));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AlternateVariant_PreservesAllFields() {
    var palette = new short[16];
    palette[0] = 0x0777;
    palette[15] = 0x0007;

    var pixelData = new byte[FullscreenKitFile.AlternatePixelDataSize];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var original = new FullscreenKitFile {
      Width = FullscreenKitFile.AlternateWidth,
      Height = FullscreenKitFile.AlternateHeight,
      Palette = palette,
      PixelData = pixelData
    };

    var bytes = FullscreenKitWriter.ToBytes(original);
    var restored = FullscreenKitReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(448));
      Assert.That(restored.Height, Is.EqualTo(272));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeroPixels() {
    var original = new FullscreenKitFile {
      Width = FullscreenKitFile.PrimaryWidth,
      Height = FullscreenKitFile.PrimaryHeight,
      Palette = new short[16],
      PixelData = new byte[FullscreenKitFile.PrimaryPixelDataSize]
    };

    var bytes = FullscreenKitWriter.ToBytes(original);
    var restored = FullscreenKitReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_RandomData() {
    var rng = new Random(42);
    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = (short)(rng.Next(0, 0x0800) & 0x0777);

    var pixelData = new byte[FullscreenKitFile.PrimaryPixelDataSize];
    rng.NextBytes(pixelData);

    var original = new FullscreenKitFile {
      Width = FullscreenKitFile.PrimaryWidth,
      Height = FullscreenKitFile.PrimaryHeight,
      Palette = palette,
      PixelData = pixelData
    };

    var bytes = FullscreenKitWriter.ToBytes(original);
    var restored = FullscreenKitReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var palette = new short[16];
    palette[0] = 0x0777;

    var pixelData = new byte[FullscreenKitFile.PrimaryPixelDataSize];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new FullscreenKitFile {
      Width = FullscreenKitFile.PrimaryWidth,
      Height = FullscreenKitFile.PrimaryHeight,
      Palette = palette,
      PixelData = pixelData
    };

    var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".kid");
    try {
      var bytes = FullscreenKitWriter.ToBytes(original);
      System.IO.File.WriteAllBytes(tempPath, bytes);

      var restored = FullscreenKitReader.FromFile(new System.IO.FileInfo(tempPath));

      Assert.Multiple(() => {
        Assert.That(restored.Width, Is.EqualTo(416));
        Assert.That(restored.Height, Is.EqualTo(274));
        Assert.That(restored.Palette, Is.EqualTo(original.Palette));
        Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      });
    } finally {
      if (System.IO.File.Exists(tempPath))
        System.IO.File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Primary() {
    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = (short)(i * 0x0111 & 0x0777);

    var chunky = new byte[416 * 274];
    for (var i = 0; i < chunky.Length; ++i)
      chunky[i] = (byte)(i % 16);

    var planar = PlanarConverter.ChunkyToAtariSt(chunky, 416, 274, 4);

    var original = new FullscreenKitFile {
      Width = 416,
      Height = 274,
      Palette = palette,
      PixelData = planar
    };

    var raw = FullscreenKitFile.ToRawImage(original);

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(416));
      Assert.That(raw.Height, Is.EqualTo(274));
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(raw.PaletteCount, Is.EqualTo(16));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_FromRawImage_Primary() {
    var rgb = new byte[16 * 3];
    for (var i = 0; i < 16; ++i) {
      rgb[i * 3] = (byte)(i * 17);
      rgb[i * 3 + 1] = (byte)(i * 17);
      rgb[i * 3 + 2] = (byte)(i * 17);
    }

    var pixels = new byte[416 * 274];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 16);

    var raw = new RawImage {
      Width = 416,
      Height = 274,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = rgb,
      PaletteCount = 16,
    };

    var file = FullscreenKitFile.FromRawImage(raw);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(416));
      Assert.That(file.Height, Is.EqualTo(274));
      Assert.That(file.Palette.Length, Is.EqualTo(16));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FullscreenKitFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FullscreenKitFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 416,
      Height = 274,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[416 * 274 * 3],
    };
    Assert.Throws<ArgumentException>(() => FullscreenKitFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongDimensions_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[320 * 200],
      Palette = new byte[16 * 3],
      PaletteCount = 16,
    };
    Assert.Throws<ArgumentException>(() => FullscreenKitFile.FromRawImage(raw));
  }

  [Test]
  [Category("Integration")]
  public void FromRawImage_AlternateDimensions_Accepted() {
    var rgb = new byte[16 * 3];
    var pixels = new byte[448 * 272];

    var raw = new RawImage {
      Width = 448,
      Height = 272,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = rgb,
      PaletteCount = 16,
    };

    var file = FullscreenKitFile.FromRawImage(raw);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(448));
      Assert.That(file.Height, Is.EqualTo(272));
    });
  }
}
