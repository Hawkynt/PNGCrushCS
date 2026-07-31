using System;
using System.Text;

namespace FileFormat.DegasIcon;

/// <summary>Assembles DEGAS Elite icon source from a <see cref="DegasIconFile"/>.</summary>
public static class DegasIconWriter {

  /// <summary>
  /// Writes the icon as the C fragment the program exported, which is what the format is.
  /// </summary>
  /// <remarks>
  /// The leading comment is not decoration. The parser requires something before every token, so a
  /// file beginning with the first <c>#define</c> would not read back — which is why the exporter
  /// always wrote one and why this does too.
  /// </remarks>
  public static byte[] ToBytes(DegasIconFile file) {
    var words = (file.Width + 15) >> 4;
    var size = words * file.Height;
    var bitmap = file.Bitmap ?? [];

    var text = new StringBuilder();
    text.Append("/* DEGAS Elite icon */\n");
    text.Append($"#define ICON_W 0x{file.Width:X}\n");
    text.Append($"#define ICON_H 0x{file.Height:X}\n");
    text.Append($"#define ICONSIZE 0x{size:X}\n");
    text.Append("int image[ICONSIZE] = {");

    for (var i = 0; i < size; ++i) {
      if (i > 0)
        text.Append(',');

      text.Append(i % 8 == 0 ? "\n\t0x" : " 0x");

      var at = i * 2;
      var word = (at < bitmap.Length ? bitmap[at] << 8 : 0) | (at + 1 < bitmap.Length ? bitmap[at + 1] : 0);
      text.Append($"{word:X4}");
    }

    text.Append("\n};\n");

    return Encoding.ASCII.GetBytes(text.ToString());
  }
}
