using System;
using System.Buffers.Binary;

namespace FileFormat.X11Puzzle;

/// <summary>Assembles a puzzle picture: the size, the palette, then a byte a pixel.</summary>
public static class X11PuzzleWriter {

  public static byte[] ToBytes(X11PuzzleFile file) {
    var pixels = file.PixelData ?? [];
    var result = new byte[X11PuzzleFile.PixelOffset + file.Width * file.Height];

    BinaryPrimitives.WriteUInt32BigEndian(result, (uint)file.Width);
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4), (uint)file.Height);
    result[8] = file.Reserved;
    (file.Palette ?? []).AsSpan(0, Math.Min((file.Palette ?? []).Length, X11PuzzleFile.PaletteSize))
      .CopyTo(result.AsSpan(X11PuzzleFile.HeaderSize));
    pixels.AsSpan(0, Math.Min(pixels.Length, file.Width * file.Height))
      .CopyTo(result.AsSpan(X11PuzzleFile.PixelOffset));

    return result;
  }
}
