using System;

namespace FileFormat.MagicPainter;

/// <summary>Assembles Magic Painter (MGP) file bytes from a <see cref="MagicPainterFile"/>.</summary>
public static class MagicPainterWriter {

  public static byte[] ToBytes(MagicPainterFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var paletteSize = file.PaletteCount * 3;
    var pixelDataSize = file.Width * file.Height;
    var totalSize = MagicPainterReader.HeaderSize + paletteSize + pixelDataSize;
    var result = new byte[totalSize];

    // Write header: width (2 LE), height (2 LE), palette count (2 LE)
    result[0] = (byte)(file.Width & 0xFF);
    result[1] = (byte)((file.Width >> 8) & 0xFF);
    result[2] = (byte)(file.Height & 0xFF);
    result[3] = (byte)((file.Height >> 8) & 0xFF);
    result[4] = (byte)(file.PaletteCount & 0xFF);
    result[5] = (byte)((file.PaletteCount >> 8) & 0xFF);

    // Write palette
    file.Palette.AsSpan(0, Math.Min(paletteSize, file.Palette.Length)).CopyTo(result.AsSpan(MagicPainterReader.HeaderSize));

    // Write pixel data
    file.PixelData.AsSpan(0, Math.Min(pixelDataSize, file.PixelData.Length)).CopyTo(result.AsSpan(MagicPainterReader.HeaderSize + paletteSize));

    return result;
  }
}
