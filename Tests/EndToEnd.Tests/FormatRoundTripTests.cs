using System;
using FileFormat.Core;
using FileFormat.Png;
using FileFormat.Bmp;
using FileFormat.Qoi;
using FileFormat.Tga;
using FileFormat.Farbfeld;
using FileFormat.Netpbm;
using FileFormat.Aai;
using FileFormat.Cmu;
using FileFormat.Hrz;
using FileFormat.Sgi;
using FileFormat.Wbmp;

namespace EndToEnd.Tests;

/// <summary>Encode → decode round-trip tests for all major formats. Verifies pixel-level fidelity.</summary>
[TestFixture]
public sealed class FormatRoundTripTests {

  // --- Lossless RGBA formats (tolerance=0) ---

  [Test]
  public void Png_Rgba32_RoundTrip() => _AssertRoundTrip<PngFile>(TestImageFactory.Gradient_8x8(), PixelFormat.Rgba32);

  [Test]
  public void Png_Rgb24_RoundTrip() => _AssertRoundTrip<PngFile>(TestImageFactory.Gradient_8x8(), PixelFormat.Rgb24);

  [Test]
  public void Png_Gray8_RoundTrip() => _AssertRoundTrip<PngFile>(TestImageFactory.Gradient_8x8(), PixelFormat.Gray8);

  [Test]
  public void Qoi_Rgba32_RoundTrip() => _AssertRoundTrip<QoiFile>(TestImageFactory.Random_64x64(), PixelFormat.Rgba32);

  [Test]
  public void Qoi_Rgb24_RoundTrip() => _AssertRoundTrip<QoiFile>(TestImageFactory.Random_64x64(), PixelFormat.Rgb24);

  [Test]
  public void Bmp_Bgr24_RoundTrip() => _AssertRoundTrip<BmpFile>(TestImageFactory.Gradient_8x8(), PixelFormat.Bgr24);

  [Test]
  public void Tga_Bgra32_RoundTrip() => _AssertRoundTrip<TgaFile>(TestImageFactory.Random_64x64(), PixelFormat.Bgra32);

  [Test]
  public void Tga_Bgr24_RoundTrip() => _AssertRoundTrip<TgaFile>(TestImageFactory.Gradient_8x8(), PixelFormat.Bgr24);

  [Test]
  public void Farbfeld_Rgba64_RoundTrip() => _AssertRoundTrip<FarbfeldFile>(TestImageFactory.Gradient_8x8(), PixelFormat.Rgba64, tolerance: 1); // 16-bit→8-bit rounding

  [Test]
  public void Aai_Rgba32_RoundTrip() => _AssertRoundTrip<AaiFile>(TestImageFactory.RedGreenBlueWhite_2x2(), PixelFormat.Rgba32);

  [Test]
  public void Sgi_Rgb24_RoundTrip() => _AssertRoundTrip<SgiFile>(TestImageFactory.Gradient_8x8(), PixelFormat.Rgb24);

  // --- Fixed-size formats ---

  [Test]
  public void Hrz_Rgb24_RoundTrip() {
    // HRZ is fixed 256x240
    var data = new byte[256 * 240 * 4];
    new Random(42).NextBytes(data);
    for (var i = 3; i < data.Length; i += 4) data[i] = 255;
    var raw = new RawImage { Width = 256, Height = 240, Format = PixelFormat.Rgba32, PixelData = data };
    _AssertRoundTrip<HrzFile>(raw, PixelFormat.Rgb24);
  }

  // --- Cross-format chain ---

  [Test]
  public void Png_To_Qoi_CrossFormat_PreservesPixels() {
    var original = TestImageFactory.Random_64x64();
    var asRgb = TestImageFactory.ConvertTo(original, PixelFormat.Rgb24);

    // Encode as PNG, decode back
    var pngBytes = FormatIO.Encode<PngFile>(asRgb);
    var fromPng = FormatIO.Decode<PngFile>(pngBytes);

    // Encode as QOI, decode back
    var qoiBytes = FormatIO.Encode<QoiFile>(fromPng);
    var fromQoi = FormatIO.Decode<QoiFile>(qoiBytes);

    PixelComparer.AssertEqual(asRgb, fromQoi, tolerance: 0, "PNG→QOI cross-format");
  }

  [Test]
  public void Bmp_To_Tga_CrossFormat_PreservesPixels() {
    var original = TestImageFactory.Gradient_8x8();
    var asBgr = TestImageFactory.ConvertTo(original, PixelFormat.Bgr24);

    var bmpBytes = FormatIO.Encode<BmpFile>(asBgr);
    var fromBmp = FormatIO.Decode<BmpFile>(bmpBytes);

    var asBgra = TestImageFactory.ConvertTo(fromBmp, PixelFormat.Bgra32);
    var tgaBytes = FormatIO.Encode<TgaFile>(asBgra);
    var fromTga = FormatIO.Decode<TgaFile>(tgaBytes);

    PixelComparer.AssertEqual(asBgr, fromTga, tolerance: 0, "BMP→TGA cross-format");
  }

  // --- GDI+ reference comparison ---

  [Test]
  public void Png_OurDecoder_MatchesGdiPlus() {
    // Create PNG with GDI+, decode with both, compare
    var original = TestImageFactory.Random_64x64();
    var pngBytes = FormatIO.Encode<PngFile>(TestImageFactory.ConvertTo(original, PixelFormat.Rgba32));

    // Decode with our reader
    var ours = FormatIO.Decode<PngFile>(pngBytes);

    // Decode with GDI+
    var tmpPath = System.IO.Path.GetTempFileName() + ".png";
    try {
      System.IO.File.WriteAllBytes(tmpPath, pngBytes);
      using var gdiBmp = new System.Drawing.Bitmap(tmpPath);
      var gdiRaw = _BitmapToRawImage(gdiBmp);
      PixelComparer.AssertEqual(ours, gdiRaw, tolerance: 0, "PNG our decoder vs GDI+");
    } finally {
      System.IO.File.Delete(tmpPath);
    }
  }

  [Test]
  public void Bmp_OurDecoder_MatchesGdiPlus() {
    var original = TestImageFactory.Random_64x64();
    var bmpBytes = FormatIO.Encode<BmpFile>(TestImageFactory.ConvertTo(original, PixelFormat.Bgr24));

    var ours = FormatIO.Decode<BmpFile>(bmpBytes);

    var tmpPath = System.IO.Path.GetTempFileName() + ".bmp";
    try {
      System.IO.File.WriteAllBytes(tmpPath, bmpBytes);
      using var gdiBmp = new System.Drawing.Bitmap(tmpPath);
      var gdiRaw = _BitmapToRawImage(gdiBmp);
      PixelComparer.AssertEqual(ours, gdiRaw, tolerance: 0, "BMP our decoder vs GDI+");
    } finally {
      System.IO.File.Delete(tmpPath);
    }
  }

  // --- Helper methods ---

  private static void _AssertRoundTrip<T>(RawImage source, PixelFormat targetFormat, int tolerance = 0)
    where T : IImageFormatReader<T>, IImageToRawImage<T>, IImageFromRawImage<T>, IImageFormatWriter<T> {
    var converted = TestImageFactory.ConvertTo(source, targetFormat);
    var encoded = FormatIO.Encode<T>(converted);
    var decoded = FormatIO.Decode<T>(encoded);
    PixelComparer.AssertEqual(converted, decoded, tolerance,
      $"{typeof(T).Name} round-trip ({targetFormat}, {converted.Width}x{converted.Height})");
  }

  private static RawImage _BitmapToRawImage(System.Drawing.Bitmap bmp) {
    var w = bmp.Width;
    var h = bmp.Height;
    var bmpData = bmp.LockBits(
      new System.Drawing.Rectangle(0, 0, w, h),
      System.Drawing.Imaging.ImageLockMode.ReadOnly,
      System.Drawing.Imaging.PixelFormat.Format32bppArgb
    );
    try {
      var stride = bmpData.Stride;
      var rowBytes = w * 4;
      var bgra = new byte[rowBytes * h];
      if (stride == rowBytes)
        System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, bgra, 0, bgra.Length);
      else
        for (var y = 0; y < h; ++y)
          System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0 + y * stride, bgra, y * rowBytes, rowBytes);
      return new() { Width = w, Height = h, Format = PixelFormat.Bgra32, PixelData = bgra };
    } finally {
      bmp.UnlockBits(bmpData);
    }
  }
}
