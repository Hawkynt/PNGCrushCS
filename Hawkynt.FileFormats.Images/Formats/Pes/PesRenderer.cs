using System;

namespace FileFormat.Pes;

/// <summary>Draws a PES's needle path.</summary>
/// <remarks>
/// The picture a PES makes is a rendering rather than something the file states:
/// the file holds a path, and how wide the thread is drawn and what lies behind
/// it are the reader's choice. What is drawn here is one path per colour block,
/// a single pixel wide, on white, over a canvas the size of the stitch bounds —
/// the same shape ImageMagick's coder draws, which turns the stitches into an
/// SVG of one stroked path per block and rasterises that.
/// </remarks>
internal static class PesRenderer {

  public static byte[] Render(PesFile file, int width, int height) {
    var pixels = new byte[checked(width * height * 3)];
    pixels.AsSpan().Fill(0xFF);

    foreach (var block in file.Blocks) {
      var r = (byte)(block.Color >> 16);
      var g = (byte)(block.Color >> 8);
      var b = (byte)block.Color;

      for (var i = 1; i < block.Points.Length; ++i) {
        var (x0, y0) = block.Points[i - 1];
        var (x1, y1) = block.Points[i];
        _Line(pixels, width, height, x0 - file.MinX, y0 - file.MinY, x1 - file.MinX, y1 - file.MinY, r, g, b);
      }
    }

    return pixels;
  }

  private static void _Line(byte[] pixels, int width, int height, int x0, int y0, int x1, int y1, byte r, byte g, byte b) {
    var dx = Math.Abs(x1 - x0);
    var dy = -Math.Abs(y1 - y0);
    var stepX = x0 < x1 ? 1 : -1;
    var stepY = y0 < y1 ? 1 : -1;
    var error = dx + dy;

    while (true) {
      if ((uint)x0 < (uint)width && (uint)y0 < (uint)height) {
        var at = (y0 * width + x0) * 3;
        pixels[at] = r;
        pixels[at + 1] = g;
        pixels[at + 2] = b;
      }

      if (x0 == x1 && y0 == y1)
        break;

      var doubled = error * 2;
      if (doubled >= dy) {
        error += dy;
        x0 += stepX;
      }

      if (doubled > dx)
        continue;

      error += dx;
      y0 += stepY;
    }
  }
}
