using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using FileFormat.Core;
using Optimizer.Png;

namespace Optimizer.Image;

/// <summary>Loads image files into <see cref="Bitmap"/> or <see cref="RawImage"/> with universal format dispatch covering 86+ formats.</summary>
internal static class BitmapConverter {

  /// <summary>Loads an image file as a <see cref="RawImage"/> using format-specific readers. Returns null if the format is unsupported or reading fails.</summary>
  internal static RawImage? LoadRawImage(FileInfo file, ImageFormat format)
    => FormatRegistry.GetEntry(format)?.LoadRawImage(file);

  /// <summary>Converts a <see cref="RawImage"/> to a GDI+ <see cref="Bitmap"/> in BGRA32 format.</summary>
  internal static Bitmap RawImageToBitmap(RawImage raw) {
    ArgumentNullException.ThrowIfNull(raw);
    var bgra = raw.ToBgra32();
    return _CreateBitmap(raw.Width, raw.Height, bgra);
  }

  /// <summary>Extracts pixel data from a GDI+ <see cref="Bitmap"/> as a BGRA32 <see cref="RawImage"/>.</summary>
  internal static RawImage BitmapToRawImage(Bitmap bmp) {
    ArgumentNullException.ThrowIfNull(bmp);
    var width = bmp.Width;
    var height = bmp.Height;
    var bmpData = bmp.LockBits(
      new Rectangle(0, 0, width, height),
      System.Drawing.Imaging.ImageLockMode.ReadOnly,
      System.Drawing.Imaging.PixelFormat.Format32bppArgb
    );
    try {
      var stride = bmpData.Stride;
      var rowBytes = width * 4;
      var bgra = new byte[rowBytes * height];
      if (stride == rowBytes)
        Marshal.Copy(bmpData.Scan0, bgra, 0, bgra.Length);
      else
        for (var y = 0; y < height; ++y)
          Marshal.Copy(bmpData.Scan0 + y * stride, bgra, y * rowBytes, rowBytes);

      return new() {
        Width = width,
        Height = height,
        Format = FileFormat.Core.PixelFormat.Bgra32,
        PixelData = bgra,
      };
    } finally {
      bmp.UnlockBits(bmpData);
    }
  }

  /// <summary>Loads an image file as a <see cref="Bitmap"/>. Tries format-specific RawImage readers first, falls back to GDI+.</summary>
  internal static Bitmap LoadBitmap(FileInfo file, ImageFormat format) {
    var raw = LoadRawImage(file, format);
    if (raw != null)
      return RawImageToBitmap(raw);

    // GDI+ fallback for formats without dedicated readers (GIF, etc.)
    return new(file.FullName);
  }

  /// <summary>Quantizes a <see cref="RawImage"/> to an indexed image with the specified max palette size using FrameworkExtensions quantizer/ditherer dispatch.</summary>
  internal static RawImage QuantizeRawImage(RawImage source, int maxColors, string quantizerName = "Median Cut", string dithererName = "ErrorDiffusion_FloydSteinberg", bool isHighQuality = false) {
    ArgumentNullException.ThrowIfNull(source);
    using var bmp = RawImageToBitmap(source);
    using var indexed = _DispatchReduceColors(bmp, quantizerName, dithererName, maxColors, isHighQuality);

    var width = indexed.Width;
    var height = indexed.Height;
    var entries = indexed.Palette.Entries;
    var paletteCount = Math.Min(entries.Length, maxColors);

    var palette = new byte[paletteCount * 3];
    byte[]? alphaTable = null;
    var hasAlpha = false;
    for (var i = 0; i < paletteCount; ++i) {
      var entry = entries[i];
      palette[i * 3] = entry.R;
      palette[i * 3 + 1] = entry.G;
      palette[i * 3 + 2] = entry.B;
      if (entry.A < 255)
        hasAlpha = true;
    }

    if (hasAlpha) {
      alphaTable = new byte[paletteCount];
      for (var i = 0; i < paletteCount; ++i)
        alphaTable[i] = entries[i].A;
    }

    var bmpData = indexed.LockBits(
      new Rectangle(0, 0, width, height),
      System.Drawing.Imaging.ImageLockMode.ReadOnly,
      indexed.PixelFormat
    );
    try {
      var stride = bmpData.Stride;
      var indices = new byte[width * height];
      for (var y = 0; y < height; ++y)
        Marshal.Copy(bmpData.Scan0 + y * stride, indices, y * width, width);

      return new() {
        Width = width,
        Height = height,
        Format = FileFormat.Core.PixelFormat.Indexed8,
        PixelData = indices,
        Palette = palette,
        PaletteCount = paletteCount,
        AlphaTable = alphaTable,
      };
    } finally {
      indexed.UnlockBits(bmpData);
    }
  }

  private static Bitmap _DispatchReduceColors(Bitmap source, string quantizerName, string dithererName, int colorCount, bool isHighQuality)
    => ReduceColorsDispatch.ReduceColors(source, quantizerName, dithererName, colorCount, isHighQuality);

  private static Bitmap _CreateBitmap(int width, int height, byte[] bgraPixels) {
    var bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    var bmpData = bmp.LockBits(
      new Rectangle(0, 0, width, height),
      System.Drawing.Imaging.ImageLockMode.WriteOnly,
      System.Drawing.Imaging.PixelFormat.Format32bppArgb
    );
    try {
      var stride = bmpData.Stride;
      var rowBytes = width * 4;
      if (stride == rowBytes)
        Marshal.Copy(bgraPixels, 0, bmpData.Scan0, bgraPixels.Length);
      else
        for (var y = 0; y < height; ++y)
          Marshal.Copy(bgraPixels, y * rowBytes, bmpData.Scan0 + y * stride, rowBytes);
    } finally {
      bmp.UnlockBits(bmpData);
    }

    return bmp;
  }
}
