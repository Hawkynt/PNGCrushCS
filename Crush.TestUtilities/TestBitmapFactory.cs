using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace Crush.TestUtilities;

/// <summary>Factory for creating test bitmaps with reproducible patterns</summary>
public static class TestBitmapFactory {

  /// <summary>Create a test bitmap with a gradient color pattern</summary>
  public static Bitmap CreateTestBitmap(int width = 8, int height = 8, bool grayscale = false, bool hasAlpha = false) {
    var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      Color c;
      if (grayscale) {
        var g = (int)(255.0 * x / width);
        var a = hasAlpha ? (int)(255.0 * y / height) : 255;
        c = Color.FromArgb(a, g, g, g);
      } else {
        var a = hasAlpha ? (int)(255.0 * y / height) : 255;
        c = Color.FromArgb(a, x * 32 % 256, y * 32 % 256, (x + y) * 16 % 256);
      }

      bmp.SetPixel(x, y, c);
    }

    return bmp;
  }
}
