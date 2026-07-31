using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace Crush.Viewer;

/// <summary>Turning a picture into something Windows Forms can draw, and back.</summary>
/// <remarks>
/// This is the only place in the tree that still needs the platform's bitmap type, and it needs it
/// for the one thing bitmaps are actually for here: putting pixels on a screen. The optimizers all
/// speak in <see cref="RawImage"/> now, so nothing else has to carry a native graphics dependency.
/// </remarks>
internal static class BitmapBridge {

  /// <summary>Draws a picture into a bitmap, blue first, which is the order both sides store.</summary>
  internal static Bitmap ToBitmap(RawImage raw) {
    ArgumentNullException.ThrowIfNull(raw);

    var bgra = PixelConverter.Convert(raw, FileFormat.Core.PixelFormat.Bgra32).PixelData;
    var bitmap = new Bitmap(raw.Width, raw.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

    var data = bitmap.LockBits(
      new Rectangle(0, 0, raw.Width, raw.Height),
      ImageLockMode.WriteOnly,
      System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    try {
      for (var y = 0; y < raw.Height; ++y)
        System.Runtime.InteropServices.Marshal.Copy(
          bgra, y * raw.Width * 4, data.Scan0 + y * data.Stride, raw.Width * 4);
    } finally {
      bitmap.UnlockBits(data);
    }

    return bitmap;
  }

  /// <summary>Reads a bitmap back into a picture.</summary>
  internal static RawImage ToRawImage(Bitmap bitmap) {
    ArgumentNullException.ThrowIfNull(bitmap);

    var width = bitmap.Width;
    var height = bitmap.Height;
    var pixels = new byte[width * height * 4];

    var data = bitmap.LockBits(
      new Rectangle(0, 0, width, height),
      ImageLockMode.ReadOnly,
      System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    try {
      for (var y = 0; y < height; ++y)
        System.Runtime.InteropServices.Marshal.Copy(
          data.Scan0 + y * data.Stride, pixels, y * width * 4, width * 4);
    } finally {
      bitmap.UnlockBits(data);
    }

    return new() {
      Width = width,
      Height = height,
      Format = FileFormat.Core.PixelFormat.Bgra32,
      PixelData = pixels,
    };
  }

  /// <summary>Loads a file for display, through this project's readers rather than the platform's.</summary>
  internal static Bitmap LoadBitmap(FileInfo file, Optimizer.Image.ImageFormat format) {
    var raw = FormatRegistry.GetEntry(_Translate(format))?.LoadRawImage(file);

    // A format neither side can read is a failure either way, but the platform still knows a few
    // container variants this project does not, so it stays as the last resort here — and only here.
    return raw != null ? ToBitmap(raw) : new Bitmap(file.FullName);
  }

  private static Hawkynt.FileFormats.Images.ImageFormat _Translate(Optimizer.Image.ImageFormat format)
    => Enum.TryParse<Hawkynt.FileFormats.Images.ImageFormat>(format.ToString(), out var translated)
      ? translated
      : Hawkynt.FileFormats.Images.ImageFormat.Unknown;
}
