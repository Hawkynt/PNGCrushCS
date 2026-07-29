using System;

namespace FileFormat.MobyDick;

/// <summary>Assembles Moby Dick paint file bytes from a MobyDickFile.</summary>
public static class MobyDickWriter {

  public static byte[] ToBytes(MobyDickFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[MobyDickFile.ExpectedFileSize];
    var offset = 0;

    // Palette (768 bytes)
    file.Palette.AsSpan(0, Math.Min(file.Palette.Length, MobyDickFile.PaletteDataSize)).CopyTo(result.AsSpan(offset));
    offset += MobyDickFile.PaletteDataSize;

    // Pixel data (64000 bytes)
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, MobyDickFile.PixelDataSize)).CopyTo(result.AsSpan(offset));

    return result;
  }
}
