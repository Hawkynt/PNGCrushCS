using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using FileFormat.TextMode;

namespace Hawkynt.ImageTransformUI;

/// <summary>
/// Rasterises an installed system font (or arbitrary <see cref="FontFamily"/>) into a
/// <see cref="BitmapFont"/> by drawing each CP437 byte through <see cref="Cp437.ToUnicode"/>
/// onto a 1-bit thresholded glyph cell. Lets the user pick any TrueType monospace font on
/// the machine — Consolas, Cascadia Mono, Lucida Console, the Mxoldschool PC fonts if
/// installed, etc. — instead of being limited to the procedurally-generated default.
/// </summary>
public static class BitmapFontRasterizer {

  /// <summary>Rasterise by font family name. Throws if the family isn't installed.</summary>
  public static BitmapFont FromSystemFont(string familyName, int cellWidth = 8, int cellHeight = 16) {
    if (string.IsNullOrEmpty(familyName)) throw new ArgumentException("Family name required.", nameof(familyName));
    using var family = new FontFamily(familyName);
    return FromFamily(family, cellWidth, cellHeight);
  }

  /// <summary>Performs the from Family operation.</summary>
  public static BitmapFont FromFamily(FontFamily family, int cellWidth = 8, int cellHeight = 16) {
    if (family is null) throw new ArgumentNullException(nameof(family));
    if (cellWidth is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(cellWidth), "1 ≤ cellWidth ≤ 8.");
    if (cellHeight is < 6 or > 32) throw new ArgumentOutOfRangeException(nameof(cellHeight), "6 ≤ cellHeight ≤ 32.");

    // Pick the largest regular/bold style that's available so we have something to rasterise.
    var style = family.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular
              : family.IsStyleAvailable(FontStyle.Bold)    ? FontStyle.Bold
              : FontStyle.Regular;

    // GraphicsUnit.Pixel + em-size = cellHeight gives us a font sized to fit the cell vertically.
    // For monospace fonts this typically produces a glyph wider than cellWidth ⇒ horizontal clip.
    // We accept clipping rather than shrinking further (a too-small font is illegible).
    using var font = new Font(family, cellHeight, style, GraphicsUnit.Pixel);

    var glyphData = new byte[256 * cellHeight];
    using var bmp = new Bitmap(cellWidth, cellHeight, PixelFormat.Format24bppRgb);
    using var g = Graphics.FromImage(bmp);
    g.InterpolationMode = InterpolationMode.NearestNeighbor;
    g.SmoothingMode = SmoothingMode.None;
    // Anti-aliasing makes the threshold lossy and produces "fat" looking glyphs at 8x16 — turn it off.
    g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
    g.PixelOffsetMode = PixelOffsetMode.None;

    using var black = new SolidBrush(Color.Black);
    using var white = new SolidBrush(Color.White);
    using var noFormat = new StringFormat(StringFormat.GenericTypographic) {
      Alignment = StringAlignment.Center,
      LineAlignment = StringAlignment.Center,
      FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
    };

    var cellRect = new RectangleF(0, 0, cellWidth, cellHeight);
    for (var cp = 0; cp < 256; ++cp) {
      g.FillRectangle(black, 0, 0, cellWidth, cellHeight);
      var uni = Cp437.ToUnicode[cp];
      if (uni != ' ' && uni != '\0')
        g.DrawString(uni.ToString(), font, white, cellRect, noFormat);
      _ThresholdToGlyphRow(bmp, glyphData, cp, cellWidth, cellHeight);
    }

    return BitmapFont.FromBytes(cellWidth, cellHeight, glyphData);
  }

  /// <summary>Returns the names of installed font families, with monospace fonts first.</summary>
  public static string[] GetInstalledMonospaceFamilies() {
    using var col = new InstalledFontCollection();
    var families = col.Families;
    var names = new System.Collections.Generic.List<string>(families.Length);
    foreach (var f in families) {
      try { names.Add(f.Name); }
      finally { f.Dispose(); }
    }
    names.Sort(StringComparer.OrdinalIgnoreCase);
    return names.ToArray();
  }

  // Read the freshly-drawn bitmap, threshold each pixel against luminance > 128, pack into row bytes
  // (one byte per row, MSB = leftmost pixel) at glyphData[codePoint * cellHeight + row].
  private static void _ThresholdToGlyphRow(Bitmap bmp, byte[] glyphData, int codePoint, int cellWidth, int cellHeight) {
    var rect = new Rectangle(0, 0, cellWidth, cellHeight);
    var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
    try {
      var stride = data.Stride;
      var rowBuf = new byte[stride];
      for (var y = 0; y < cellHeight; ++y) {
        Marshal.Copy(data.Scan0 + y * stride, rowBuf, 0, stride);
        byte rowBits = 0;
        for (var x = 0; x < cellWidth; ++x) {
          var off = x * 3;
          var b = rowBuf[off];
          var gn = rowBuf[off + 1];
          var r = rowBuf[off + 2];
          // Luminance: 0.299R + 0.587G + 0.114B, integer approximation.
          var lum = (299 * r + 587 * gn + 114 * b + 500) / 1000;
          if (lum > 128) rowBits |= (byte)(1 << (7 - x));
        }
        glyphData[codePoint * cellHeight + y] = rowBits;
      }
    } finally {
      bmp.UnlockBits(data);
    }
  }
}
