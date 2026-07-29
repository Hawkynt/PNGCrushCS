using System;

namespace FileFormat.Graphics9Plus;

/// <summary>Assembles Atari 8-bit Graphics 9+ (.gr9p) screens. bytes.</summary>
public static class Graphics9PlusWriter {

  public static byte[] ToBytes(Graphics9PlusFile file) {
    var result = new byte[Graphics9PlusFile.FileSize];

    var header = file.Header ?? [];
    header.AsSpan(0, Math.Min(header.Length, Graphics9PlusFile.HeaderSize)).CopyTo(result);

    var screen = file.ScreenData ?? [];
    screen.AsSpan(0, Math.Min(screen.Length, Graphics9PlusFile.ScreenDataSize))
      .CopyTo(result.AsSpan(Graphics9PlusFile.HeaderSize));

    return result;
  }
}
