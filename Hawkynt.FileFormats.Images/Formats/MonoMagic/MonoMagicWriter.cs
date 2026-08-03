using System;

namespace FileFormat.MonoMagic;

/// <summary>Assembles Mono Magic C64 image file bytes.</summary>
public static class MonoMagicWriter {

  public static byte[] ToBytes(MonoMagicFile file) {
    ArgumentNullException.ThrowIfNull(file);
    // A load address, then the screen a character cell at a time, then the 192 bytes it does not
    // reach into. This wrote the rows out as they stood with no load address at all.
    var result = new byte[MonoMagicFile.FileSize];
    result[0] = 0x00;
    result[1] = 0x20;
    MonoMagicFile.RowsToCells(file.PixelData ?? []).CopyTo(result.AsSpan(MonoMagicFile.ScreenOffset));
    return result;
  }
}
