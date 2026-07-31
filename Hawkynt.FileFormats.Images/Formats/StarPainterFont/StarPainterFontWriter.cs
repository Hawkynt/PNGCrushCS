using System;

namespace FileFormat.StarPainterFont;

/// <summary>Assembles a Star Painter character set from a <see cref="StarPainterFontFile"/>.</summary>
public static class StarPainterFontWriter {

  /// <summary>Writes the file, load address and all.</summary>
  /// <remarks>
  /// The two leading bytes are the address the set loads at rather than a signature, but a reader
  /// has nothing else to go on and checks them, so they are written whether or not the picture came
  /// from a file that had them.
  /// </remarks>
  public static byte[] ToBytes(StarPainterFontFile file) {
    var source = file.Data ?? [];
    var data = new byte[StarPainterFontFile.FileSize];
    source.AsSpan(0, Math.Min(source.Length, data.Length)).CopyTo(data);

    StarPainterFontFile.Signature.CopyTo(data);

    return data;
  }
}
