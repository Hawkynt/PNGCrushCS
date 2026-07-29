using System;
using System.IO;

namespace FileFormat.DaliCompressed;

/// <summary>Assembles compressed Atari ST Dali screen bytes.</summary>
public static class DaliCompressedWriter {

  public static byte[] ToBytes(DaliCompressedFile file) {
    var (counts, values) = DaliCompressor.Compress(file.ScreenData ?? new byte[DaliCompressor.ScreenSize]);

    using var ms = new MemoryStream();
    var palette = file.Palette ?? [];
    var block = new byte[DaliCompressedFile.PaletteSize];
    palette.AsSpan(0, Math.Min(palette.Length, block.Length)).CopyTo(block);
    ms.Write(block);

    ms.Write(DaliCompressedFile.FormatLength(counts.Length));
    ms.Write(DaliCompressedFile.FormatLength(values.Length));
    ms.Write(counts);
    ms.Write(values);

    return ms.ToArray();
  }
}
