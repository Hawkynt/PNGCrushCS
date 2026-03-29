using System;

namespace FileFormat.DrawIt;

/// <summary>Assembles DrawIt (DIT) file bytes from a DrawItFile.</summary>
public static class DrawItWriter {

  public static byte[] ToBytes(DrawItFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelCount = file.Width * file.Height;
    var fileSize = DrawItHeader.StructSize + DrawItFile.PaletteDataSize + pixelCount;
    var result = new byte[fileSize];
    var span = result.AsSpan();

    var header = new DrawItHeader((ushort)file.Width, (ushort)file.Height);
    header.WriteTo(span);

    file.Palette.AsSpan(0, Math.Min(file.Palette.Length, DrawItFile.PaletteDataSize))
      .CopyTo(result.AsSpan(DrawItHeader.StructSize));

    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, pixelCount))
      .CopyTo(result.AsSpan(DrawItHeader.StructSize + DrawItFile.PaletteDataSize));

    return result;
  }
}
