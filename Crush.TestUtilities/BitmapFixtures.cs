using System;
using System.Drawing;
using System.Drawing.Imaging;
using FileFormat.Core;

namespace Crush.TestUtilities;

/// <summary>Turns the bitmap fixtures these tests draw into the picture the optimizers now take.</summary>
/// <remarks>
/// The optimizers speak in <see cref="RawImage"/> so that they run anywhere. Their tests still build
/// fixtures with GDI+ and read results back through it, so they remain a Windows fixture — this is
/// the one place the two meet, rather than a converter per test project.
/// </remarks>
public static class BitmapFixtures {

  /// <summary>Reads a bitmap into a picture, blue first, which is the order both sides store.</summary>
  public static RawImage ToRawImage(this Bitmap source) {
    ArgumentNullException.ThrowIfNull(source);

    var width = source.Width;
    var height = source.Height;
    var pixels = new byte[width * height * 4];

    var data = source.LockBits(
      new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    try {
      for (var y = 0; y < height; ++y)
        System.Runtime.InteropServices.Marshal.Copy(
          data.Scan0 + y * data.Stride, pixels, y * width * 4, width * 4);
    } finally {
      source.UnlockBits(data);
    }

    return new() {
      Width = width,
      Height = height,
      Format = FileFormat.Core.PixelFormat.Bgra32,
      PixelData = pixels,
    };
  }
}
