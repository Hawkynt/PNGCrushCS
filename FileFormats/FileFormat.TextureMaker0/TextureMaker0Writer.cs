using System;

namespace FileFormat.TextureMaker0;

/// <summary>Assembles Atari 8-bit Texture Maker0 (.tx0) file bytes.</summary>
public static class TextureMaker0Writer {

  public static byte[] ToBytes(TextureMaker0File file) {
    var result = new byte[TextureMaker0File.FileSize];

    var texels = file.TexelData ?? [];
    texels.AsSpan(0, Math.Min(texels.Length, TextureMaker0File.TexelDataSize)).CopyTo(result);
    result[TextureMaker0File.ColorOffset] = file.Color;

    return result;
  }
}
