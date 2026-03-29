using System;
using System.IO;
using System.Text;

namespace FileFormat.Sixel;

/// <summary>Assembles Sixel (DEC terminal graphics) file bytes from a SixelFile.</summary>
public static class SixelWriter {

  public static byte[] ToBytes(SixelFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var sb = new StringBuilder();

    sb.Append('\x1B');
    sb.Append('P');
    sb.Append(file.AspectRatio);
    sb.Append(';');
    sb.Append(file.BackgroundMode);
    sb.Append(";0q");

    var body = SixelCodec.Encode(file.PixelData, file.Width, file.Height, file.Palette, file.PaletteColorCount);
    sb.Append(body);

    sb.Append('\x1B');
    sb.Append('\\');

    return Encoding.ASCII.GetBytes(sb.ToString());
  }
}
