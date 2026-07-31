using System;
using System.Text;

namespace FileFormat.DaisyDotFont;

/// <summary>Assembles a Daisy-Dot NLQ font from a <see cref="DaisyDotFontFile"/>.</summary>
public static class DaisyDotFontWriter {

  public static byte[] ToBytes(DaisyDotFontFile file) {
    var data = file.Data ?? [];
    var result = new byte[data.Length];
    data.AsSpan().CopyTo(result);

    return result;
  }

  /// <summary>The bytes a font's header occupies: the signature and the line it ends with.</summary>
  internal static void WriteHeader(Span<byte> data) {
    Encoding.ASCII.GetBytes(DaisyDotFontFile.Signature).CopyTo(data);
    data[DaisyDotFontFile.CharactersOffset - 1] = DaisyDotFontFile.Terminator;
  }
}
