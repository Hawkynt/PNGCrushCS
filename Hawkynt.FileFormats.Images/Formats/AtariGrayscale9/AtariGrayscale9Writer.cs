using System;

namespace FileFormat.AtariGrayscale9;

/// <summary>Assembles Atari 8-bit Graphics 9 greyscale (.bg9/.g09) screens. bytes.</summary>
public static class AtariGrayscale9Writer {

  public static byte[] ToBytes(AtariGrayscale9File file) {
    var result = new byte[AtariGrayscale9File.FileSize];

    var header = file.Header ?? [];
    header.AsSpan(0, Math.Min(header.Length, AtariGrayscale9File.HeaderSize)).CopyTo(result);

    var screen = file.ScreenData ?? [];
    screen.AsSpan(0, Math.Min(screen.Length, AtariGrayscale9File.ScreenDataSize))
      .CopyTo(result.AsSpan(AtariGrayscale9File.HeaderSize));

    return result;
  }
}
