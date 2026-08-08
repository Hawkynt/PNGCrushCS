using System;
using System.Globalization;
using System.Text;

namespace FileFormat.Taac;

/// <summary>Writes a Sun TAAC bitmap: the four letters, the text header, a form feed, then the raster.</summary>
/// <remarks>
/// The header states everything the reader checks and states it the way the reader checks it —
/// <c>rank</c> and as many extents in <c>size</c>, <c>colormapsize</c> and exactly that many entries
/// in <c>colormap</c> — so a file this writes is one it accepts by its own arithmetic rather than by
/// landing on plausible numbers.
/// <para/>
/// The colour map is written blue first, which is the order xloadimage reads it in and the order the
/// one sample settles. Written the obvious way round the skin in that photograph comes out blue, so
/// a writer that emitted red first would round-trip through this reader and produce a file every
/// other tool drew wrong.
/// </remarks>
public static class TaacWriter {

  public static byte[] ToBytes(TaacFile file) {
    var width = file.Width;
    var height = file.Height;
    if (width < 1 || height < 1)
      throw new ArgumentException($"Invalid TAAC bitmap size: {width}x{height}.", nameof(file));

    var bands = file.Bands;
    if (bands is not (1 or 3))
      throw new ArgumentException($"A TAAC bitmap carries one band or three, not {bands}.", nameof(file));

    var pixels = file.PixelData ?? new byte[width * height * bands];
    var needed = width * height * bands;
    if (pixels.Length < needed)
      throw new ArgumentException($"A TAAC bitmap of {width} by {height} in {bands} band(s) needs {needed} bytes and has {pixels.Length}.", nameof(file));

    var header = new StringBuilder();
    header.Append(TaacFile.Magic).Append('\n');
    header.Append("rank=2;\n");
    header.Append("type=raster;\n");
    header.Append("format=slice;\n");
    header.Append("bits=8;\n");
    header.Append(CultureInfo.InvariantCulture, $"bands={bands};\n");
    header.Append(CultureInfo.InvariantCulture, $"size={width} {height};\n");

    var count = file.PaletteCount;
    if (bands == 1 && file.Palette is { Length: > 0 } palette && count > 0) {
      if (count * 3 > palette.Length)
        throw new ArgumentException($"A colour map of {count} entries needs {count * 3} bytes and has {palette.Length}.", nameof(file));

      header.Append(CultureInfo.InvariantCulture, $"colormapsize={count};\n");
      header.Append("colormap=");
      for (var i = 0; i < count; ++i) {
        if (i > 0)
          header.Append(i % 8 == 0 ? '\n' : ' ');

        // Blue, green, red — the order the format keeps them in.
        header.Append(palette[i * 3 + 2].ToString("x2", CultureInfo.InvariantCulture));
        header.Append(palette[i * 3 + 1].ToString("x2", CultureInfo.InvariantCulture));
        header.Append(palette[i * 3].ToString("x2", CultureInfo.InvariantCulture));
      }

      header.Append(";\n");
    }

    var text = Encoding.Latin1.GetBytes(header.ToString());
    var result = new byte[text.Length + 2 + needed];
    text.CopyTo(result, 0);

    // The form feed ends the header and the newline ends its line; the reader steps over both.
    result[text.Length] = TaacFile.HeaderTerminator;
    result[text.Length + 1] = (byte)'\n';
    Array.Copy(pixels, 0, result, text.Length + 2, needed);

    return result;
  }
}
