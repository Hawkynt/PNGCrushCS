using System;

namespace FileFormat.Zoom4;

/// <summary>Assembles Atari 8-bit Zoom-4 graphics editor (.zm4) screens. bytes.</summary>
public static class Zoom4Writer {

  public static byte[] ToBytes(Zoom4File file) {
    var result = new byte[Zoom4File.FileSize];

    var header = file.Header ?? [];
    header.AsSpan(0, Math.Min(header.Length, Zoom4File.HeaderSize)).CopyTo(result);

    var screen = file.ScreenData ?? [];
    screen.AsSpan(0, Math.Min(screen.Length, Zoom4File.ScreenDataSize))
      .CopyTo(result.AsSpan(Zoom4File.HeaderSize));

    return result;
  }
}
