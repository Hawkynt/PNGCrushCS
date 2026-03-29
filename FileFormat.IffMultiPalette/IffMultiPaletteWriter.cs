using System;

namespace FileFormat.IffMultiPalette;

/// <summary>Assembles IFF Multi-Palette bytes from an <see cref="IffMultiPaletteFile"/>.</summary>
public static class IffMultiPaletteWriter {

  public static byte[] ToBytes(IffMultiPaletteFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[file.RawData.Length];
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result);
    return result;
  }
}
