using System;
using System.Globalization;
using System.Text;

namespace FileFormat.XbmColor;

public static class XbmColorWriter {

  public static byte[] ToBytes(XbmColorFile file) {
    ArgumentNullException.ThrowIfNull(file.Palette);
    ArgumentNullException.ThrowIfNull(file.PixelData);
    var name = string.IsNullOrWhiteSpace(file.Name) ? "image" : file.Name;
    var sb = new StringBuilder();
    sb.Append("#define ").Append(name).Append("_width ").Append(file.Width.ToString(CultureInfo.InvariantCulture)).Append('\n');
    sb.Append("#define ").Append(name).Append("_height ").Append(file.Height.ToString(CultureInfo.InvariantCulture)).Append('\n');
    sb.Append("#define ").Append(name).Append("_colors ").Append(file.ColorCount.ToString(CultureInfo.InvariantCulture)).Append('\n');

    sb.Append("static unsigned char ").Append(name).Append("_palette[] = {\n  ");
    for (var i = 0; i < file.Palette.Length; ++i) {
      if (i > 0) sb.Append(", ");
      if (i > 0 && i % 18 == 0) sb.Append("\n  ");
      sb.Append("0x").Append(file.Palette[i].ToString("X2", CultureInfo.InvariantCulture));
    }
    sb.Append("\n};\n");

    sb.Append("static unsigned char ").Append(name).Append("_pixels[] = {\n  ");
    for (var i = 0; i < file.PixelData.Length; ++i) {
      if (i > 0) sb.Append(", ");
      if (i > 0 && i % 16 == 0) sb.Append("\n  ");
      sb.Append("0x").Append(file.PixelData[i].ToString("X2", CultureInfo.InvariantCulture));
    }
    sb.Append("\n};\n");
    return Encoding.ASCII.GetBytes(sb.ToString());
  }
}
