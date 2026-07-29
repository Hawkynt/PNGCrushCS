using System;

namespace FileFormat.TextureEditorMikey;

/// <summary>Assembles Atari 8-bit Texture Editor by Mikey (.txe) screens. bytes.</summary>
public static class TextureEditorMikeyWriter {

  public static byte[] ToBytes(TextureEditorMikeyFile file) {
    var result = new byte[TextureEditorMikeyFile.FileSize];

    var header = file.Header ?? [];
    header.AsSpan(0, Math.Min(header.Length, TextureEditorMikeyFile.HeaderSize)).CopyTo(result);

    var screen = file.ScreenData ?? [];
    screen.AsSpan(0, Math.Min(screen.Length, TextureEditorMikeyFile.ScreenDataSize))
      .CopyTo(result.AsSpan(TextureEditorMikeyFile.HeaderSize));

    return result;
  }
}
