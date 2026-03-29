using System.Drawing;
using FileFormat.Bmp;
using FileFormat.Core;
using FileFormat.Farbfeld;
using FileFormat.Pcx;
using FileFormat.Qoi;
using FileFormat.Sgi;
using FileFormat.Tga;
using Optimizer.Image;

namespace Optimizer.Image.Tests;

[TestFixture]
public sealed class BitmapConverterTests {

  private string? _tempFile;

  [TearDown]
  public void TearDown() {
    if (_tempFile != null && File.Exists(_tempFile))
      File.Delete(_tempFile);
    _tempFile = null;
  }

  private FileInfo _WriteTempFile(byte[] data, string extension) {
    _tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{extension}");
    File.WriteAllBytes(_tempFile, data);
    return new FileInfo(_tempFile);
  }

  [Test]
  public void LoadBitmap_TgaRgb24_ReturnsBitmapWithCorrectDimensionsAndPixels() {
    var pixelData = new byte[] { 0, 128, 255, 50, 100, 200 };
    var file = new TgaFile {
      Width = 2, Height = 1, BitsPerPixel = 24,
      PixelData = pixelData,
      ColorMode = TgaColorMode.Rgb24,
      Compression = TgaCompression.None,
      Origin = TgaOrigin.TopLeft
    };
    var fi = _WriteTempFile(TgaWriter.ToBytes(file), ".tga");

    using var bmp = BitmapConverter.LoadBitmap(fi, ImageFormat.Tga);

    Assert.That(bmp.Width, Is.EqualTo(2));
    Assert.That(bmp.Height, Is.EqualTo(1));
    var p0 = bmp.GetPixel(0, 0);
    Assert.That(p0.B, Is.EqualTo(0));
    Assert.That(p0.G, Is.EqualTo(128));
    Assert.That(p0.R, Is.EqualTo(255));
  }

  [Test]
  public void LoadBitmap_TgaRgba32_PreservesAlpha() {
    var pixelData = new byte[] { 10, 20, 30, 128 };
    var file = new TgaFile {
      Width = 1, Height = 1, BitsPerPixel = 32,
      PixelData = pixelData,
      ColorMode = TgaColorMode.Rgba32,
      Compression = TgaCompression.None,
      Origin = TgaOrigin.TopLeft
    };
    var fi = _WriteTempFile(TgaWriter.ToBytes(file), ".tga");

    using var bmp = BitmapConverter.LoadBitmap(fi, ImageFormat.Tga);

    var p = bmp.GetPixel(0, 0);
    Assert.That(p.B, Is.EqualTo(10));
    Assert.That(p.G, Is.EqualTo(20));
    Assert.That(p.R, Is.EqualTo(30));
    Assert.That(p.A, Is.EqualTo(128));
  }

  [Test]
  public void LoadBitmap_TgaGrayscale8_ConvertsCorrectly() {
    var pixelData = new byte[] { 100 };
    var file = new TgaFile {
      Width = 1, Height = 1, BitsPerPixel = 8,
      PixelData = pixelData,
      ColorMode = TgaColorMode.Grayscale8,
      Compression = TgaCompression.None,
      Origin = TgaOrigin.TopLeft
    };
    var fi = _WriteTempFile(TgaWriter.ToBytes(file), ".tga");

    using var bmp = BitmapConverter.LoadBitmap(fi, ImageFormat.Tga);

    var p = bmp.GetPixel(0, 0);
    Assert.That(p.R, Is.EqualTo(100));
    Assert.That(p.G, Is.EqualTo(100));
    Assert.That(p.B, Is.EqualTo(100));
    Assert.That(p.A, Is.EqualTo(255));
  }

  [Test]
  public void LoadBitmap_TgaIndexed8_ResolvesFromPalette() {
    var palette = new byte[] { 255, 0, 0, 0, 255, 0 };
    var pixelData = new byte[] { 0, 1 };
    var file = new TgaFile {
      Width = 2, Height = 1, BitsPerPixel = 8,
      PixelData = pixelData,
      Palette = palette,
      PaletteColorCount = 2,
      ColorMode = TgaColorMode.Indexed8,
      Compression = TgaCompression.None,
      Origin = TgaOrigin.TopLeft
    };
    var fi = _WriteTempFile(TgaWriter.ToBytes(file), ".tga");

    using var bmp = BitmapConverter.LoadBitmap(fi, ImageFormat.Tga);

    var p0 = bmp.GetPixel(0, 0);
    Assert.That(p0.R, Is.EqualTo(255));
    Assert.That(p0.G, Is.EqualTo(0));
    Assert.That(p0.B, Is.EqualTo(0));
    var p1 = bmp.GetPixel(1, 0);
    Assert.That(p1.R, Is.EqualTo(0));
    Assert.That(p1.G, Is.EqualTo(255));
    Assert.That(p1.B, Is.EqualTo(0));
  }

  [Test]
  public void LoadBitmap_PcxRgb24_ReturnsBitmapWithCorrectPixels() {
    var pixelData = new byte[] { 200, 100, 50 };
    var file = new PcxFile {
      Width = 1, Height = 1, BitsPerPixel = 24,
      PixelData = pixelData,
      ColorMode = PcxColorMode.Rgb24,
      PlaneConfig = PcxPlaneConfig.SeparatePlanes
    };
    var fi = _WriteTempFile(PcxWriter.ToBytes(file), ".pcx");

    using var bmp = BitmapConverter.LoadBitmap(fi, ImageFormat.Pcx);

    Assert.That(bmp.Width, Is.EqualTo(1));
    Assert.That(bmp.Height, Is.EqualTo(1));
    var p = bmp.GetPixel(0, 0);
    Assert.That(p.R, Is.EqualTo(200));
    Assert.That(p.G, Is.EqualTo(100));
    Assert.That(p.B, Is.EqualTo(50));
  }

  [Test]
  public void LoadBitmap_PcxIndexed8_ResolvesFromPalette() {
    var palette = new byte[256 * 3];
    palette[0] = 0; palette[1] = 0; palette[2] = 255;
    var pixelData = new byte[] { 0 };
    var file = new PcxFile {
      Width = 1, Height = 1, BitsPerPixel = 8,
      PixelData = pixelData,
      Palette = palette,
      PaletteColorCount = 256,
      ColorMode = PcxColorMode.Indexed8
    };
    var fi = _WriteTempFile(PcxWriter.ToBytes(file), ".pcx");

    using var bmp = BitmapConverter.LoadBitmap(fi, ImageFormat.Pcx);

    var p = bmp.GetPixel(0, 0);
    Assert.That(p.R, Is.EqualTo(0));
    Assert.That(p.G, Is.EqualTo(0));
    Assert.That(p.B, Is.EqualTo(255));
  }

  [Test]
  public void LoadBitmap_QoiRgb_ReturnsBitmapWithCorrectPixels() {
    var pixelData = new byte[] { 255, 0, 128 };
    var file = new QoiFile {
      Width = 1, Height = 1,
      Channels = QoiChannels.Rgb,
      ColorSpace = QoiColorSpace.Srgb,
      PixelData = pixelData
    };
    var fi = _WriteTempFile(QoiWriter.ToBytes(file), ".qoi");

    using var bmp = BitmapConverter.LoadBitmap(fi, ImageFormat.Qoi);

    var p = bmp.GetPixel(0, 0);
    Assert.That(p.R, Is.EqualTo(255));
    Assert.That(p.G, Is.EqualTo(0));
    Assert.That(p.B, Is.EqualTo(128));
    Assert.That(p.A, Is.EqualTo(255));
  }

  [Test]
  public void LoadBitmap_QoiRgba_PreservesAlpha() {
    var pixelData = new byte[] { 100, 150, 200, 64 };
    var file = new QoiFile {
      Width = 1, Height = 1,
      Channels = QoiChannels.Rgba,
      ColorSpace = QoiColorSpace.Srgb,
      PixelData = pixelData
    };
    var fi = _WriteTempFile(QoiWriter.ToBytes(file), ".qoi");

    using var bmp = BitmapConverter.LoadBitmap(fi, ImageFormat.Qoi);

    var p = bmp.GetPixel(0, 0);
    Assert.That(p.R, Is.EqualTo(100));
    Assert.That(p.G, Is.EqualTo(150));
    Assert.That(p.B, Is.EqualTo(200));
    Assert.That(p.A, Is.EqualTo(64));
  }

  [Test]
  public void LoadBitmap_Farbfeld_DownscalesAndConverts() {
    var pixelData = new byte[8];
    pixelData[0] = 0xFF; pixelData[1] = 0x00;
    pixelData[2] = 0x80; pixelData[3] = 0x00;
    pixelData[4] = 0x40; pixelData[5] = 0x00;
    pixelData[6] = 0xC0; pixelData[7] = 0x00;
    var file = new FarbfeldFile {
      Width = 1, Height = 1,
      PixelData = pixelData
    };
    var fi = _WriteTempFile(FarbfeldWriter.ToBytes(file), ".ff");

    using var bmp = BitmapConverter.LoadBitmap(fi, ImageFormat.Farbfeld);

    var p = bmp.GetPixel(0, 0);
    Assert.That(p.R, Is.EqualTo(0xFF));
    Assert.That(p.G, Is.EqualTo(0x80));
    Assert.That(p.B, Is.EqualTo(0x40));
    Assert.That(p.A, Is.EqualTo(0xC0));
  }

  [Test]
  public void LoadBitmap_Png_UsesGdiPlusFallback() {
    var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");
    _tempFile = tempPath;
    using (var img = new Bitmap(3, 2, System.Drawing.Imaging.PixelFormat.Format32bppArgb)) {
      img.SetPixel(0, 0, Color.Red);
      img.Save(tempPath, System.Drawing.Imaging.ImageFormat.Png);
    }

    using var bmp = BitmapConverter.LoadBitmap(new FileInfo(tempPath), ImageFormat.Png);

    Assert.That(bmp.Width, Is.EqualTo(3));
    Assert.That(bmp.Height, Is.EqualTo(2));
    var p = bmp.GetPixel(0, 0);
    Assert.That(p.R, Is.EqualTo(255));
    Assert.That(p.G, Is.EqualTo(0));
    Assert.That(p.B, Is.EqualTo(0));
  }

  [Test]
  public void LoadBitmap_Bmp_UsesGdiPlusFallback() {
    var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.bmp");
    _tempFile = tempPath;
    using (var img = new Bitmap(2, 2, System.Drawing.Imaging.PixelFormat.Format24bppRgb)) {
      img.SetPixel(1, 0, Color.Blue);
      img.Save(tempPath, System.Drawing.Imaging.ImageFormat.Bmp);
    }

    using var bmp = BitmapConverter.LoadBitmap(new FileInfo(tempPath), ImageFormat.Bmp);

    Assert.That(bmp.Width, Is.EqualTo(2));
    Assert.That(bmp.Height, Is.EqualTo(2));
    var p = bmp.GetPixel(1, 0);
    Assert.That(p.B, Is.EqualTo(255));
  }

  [Test]
  public void LoadBitmap_TgaMultiPixel_PreservesAllPixels() {
    var pixelData = new byte[] {
      255, 0, 0, 255,
      0, 255, 0, 128,
      0, 0, 255, 64,
      128, 128, 128, 255
    };
    var file = new TgaFile {
      Width = 2, Height = 2, BitsPerPixel = 32,
      PixelData = pixelData,
      ColorMode = TgaColorMode.Rgba32,
      Compression = TgaCompression.None,
      Origin = TgaOrigin.TopLeft
    };
    var fi = _WriteTempFile(TgaWriter.ToBytes(file), ".tga");

    using var bmp = BitmapConverter.LoadBitmap(fi, ImageFormat.Tga);

    Assert.That(bmp.Width, Is.EqualTo(2));
    Assert.That(bmp.Height, Is.EqualTo(2));
    var p00 = bmp.GetPixel(0, 0);
    Assert.That(p00.B, Is.EqualTo(255));
    Assert.That(p00.A, Is.EqualTo(255));
    var p10 = bmp.GetPixel(1, 0);
    Assert.That(p10.G, Is.EqualTo(255));
    Assert.That(p10.A, Is.EqualTo(128));
  }

  [Test]
  public void RawImageToBitmap_Bgra32_ProducesCorrectBitmap() {
    var raw = new RawImage {
      Width = 2,
      Height = 2,
      Format = FileFormat.Core.PixelFormat.Bgra32,
      PixelData = [
        255, 0, 0, 255,     // B=255, G=0, R=0, A=255
        0, 255, 0, 255,     // B=0, G=255, R=0, A=255
        0, 0, 255, 255,     // B=0, G=0, R=255, A=255
        128, 128, 128, 200   // B=128, G=128, R=128, A=200
      ]
    };

    using var bmp = BitmapConverter.RawImageToBitmap(raw);

    Assert.That(bmp.Width, Is.EqualTo(2));
    Assert.That(bmp.Height, Is.EqualTo(2));
    var p00 = bmp.GetPixel(0, 0);
    Assert.That(p00.R, Is.EqualTo(0));
    Assert.That(p00.G, Is.EqualTo(0));
    Assert.That(p00.B, Is.EqualTo(255));
    Assert.That(p00.A, Is.EqualTo(255));
    var p10 = bmp.GetPixel(1, 0);
    Assert.That(p10.R, Is.EqualTo(0));
    Assert.That(p10.G, Is.EqualTo(255));
    Assert.That(p10.B, Is.EqualTo(0));
    var p01 = bmp.GetPixel(0, 1);
    Assert.That(p01.R, Is.EqualTo(255));
    Assert.That(p01.G, Is.EqualTo(0));
    Assert.That(p01.B, Is.EqualTo(0));
    var p11 = bmp.GetPixel(1, 1);
    Assert.That(p11.R, Is.EqualTo(128));
    Assert.That(p11.A, Is.EqualTo(200));
  }

  [Test]
  public void BitmapToRawImage_RoundTrip_PreservesPixels() {
    using var bmp = new Bitmap(2, 2, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    bmp.SetPixel(0, 0, Color.FromArgb(255, 255, 0, 0));
    bmp.SetPixel(1, 0, Color.FromArgb(255, 0, 255, 0));
    bmp.SetPixel(0, 1, Color.FromArgb(255, 0, 0, 255));
    bmp.SetPixel(1, 1, Color.FromArgb(128, 64, 32, 16));

    var raw = BitmapConverter.BitmapToRawImage(bmp);

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(2));
      Assert.That(raw.Height, Is.EqualTo(2));
      Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Bgra32));
      Assert.That(raw.PixelData.Length, Is.EqualTo(2 * 2 * 4));
    });

    Assert.That(raw.PixelData[0], Is.EqualTo(0));   // B of (255, 0, 0)
    Assert.That(raw.PixelData[1], Is.EqualTo(0));   // G
    Assert.That(raw.PixelData[2], Is.EqualTo(255)); // R
    Assert.That(raw.PixelData[3], Is.EqualTo(255)); // A
    Assert.That(raw.PixelData[4], Is.EqualTo(0));   // B of (0, 255, 0)
    Assert.That(raw.PixelData[5], Is.EqualTo(255)); // G
    Assert.That(raw.PixelData[6], Is.EqualTo(0));   // R
    Assert.That(raw.PixelData[7], Is.EqualTo(255)); // A
  }

  [Test]
  public void LoadRawImage_Sgi_ReturnsRawImage() {
    var raw = new RawImage {
      Width = 4,
      Height = 4,
      Format = FileFormat.Core.PixelFormat.Rgb24,
      PixelData = new byte[4 * 4 * 3]
    };
    for (var i = 0; i < raw.PixelData.Length; ++i)
      raw.PixelData[i] = (byte)(i & 0xFF);

    var sgiFile = SgiFile.FromRawImage(raw);
    var bytes = SgiWriter.ToBytes(sgiFile);
    var fi = _WriteTempFile(bytes, ".sgi");

    var loaded = BitmapConverter.LoadRawImage(fi, ImageFormat.Sgi);

    Assert.That(loaded, Is.Not.Null);
    Assert.That(loaded!.Width, Is.EqualTo(4));
    Assert.That(loaded.Height, Is.EqualTo(4));
  }

  [Test]
  public void QuantizeRawImage_Truecolor_ProducesIndexed8() {
    var width = 32;
    var height = 32;
    var pixelData = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var o = (y * width + x) * 4;
        pixelData[o] = (byte)(x * 8);
        pixelData[o + 1] = (byte)(y * 8);
        pixelData[o + 2] = (byte)((x + y) * 4);
        pixelData[o + 3] = 255;
      }

    var source = new RawImage {
      Width = width,
      Height = height,
      Format = FileFormat.Core.PixelFormat.Bgra32,
      PixelData = pixelData
    };

    var result = BitmapConverter.QuantizeRawImage(source, 256);

    Assert.Multiple(() => {
      Assert.That(result.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Indexed8));
      Assert.That(result.PaletteCount, Is.LessThanOrEqualTo(256));
      Assert.That(result.PaletteCount, Is.GreaterThan(0));
      Assert.That(result.PixelData.Length, Is.EqualTo(width * height));
      Assert.That(result.Width, Is.EqualTo(width));
      Assert.That(result.Height, Is.EqualTo(height));
    });
  }
}
