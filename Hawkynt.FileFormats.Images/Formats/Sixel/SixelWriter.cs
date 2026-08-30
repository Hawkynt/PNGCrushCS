using System;
using System.Text;

namespace FileFormat.Sixel;

/// <summary>Assembles SIXEL (DEC terminal graphics) file bytes from a <see cref="SixelFile"/>.</summary>
public static class SixelWriter {

  public static byte[] ToBytes(SixelFile file) {
    if (file.Width < 1 || file.Height < 1)
      throw new ArgumentException("SIXEL requires positive image dimensions.", nameof(file));
    if (file.PixelData == null || file.PixelData.Length != checked(file.Width * file.Height))
      throw new ArgumentException("SIXEL pixel data length does not match width and height.", nameof(file));

    var sb = new StringBuilder();
    sb.Append('\x1B').Append('P').Append(file.AspectRatio).Append(";1;0q");

    // The encoder writes one plane per colour and returns to the left edge between planes. With P2=0
    // a zero bit in a later plane paints the terminal background and erases colours already emitted.
    // P2=1 is therefore part of the encoding strategy, not merely a caller preference.
    sb.Append(SixelCodec.Encode(file.PixelData, file.Width, file.Height, file.Palette, file.PaletteColorCount));

    sb.Append('\x1B').Append('\\');
    return Encoding.ASCII.GetBytes(sb.ToString());
  }
}
