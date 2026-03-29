using System;
using System.Globalization;
using System.Text;

namespace FileFormat.Xpm;

/// <summary>Assembles XPM file bytes from an <see cref="XpmFile"/>.</summary>
public static class XpmWriter {

  private const string _DEFAULT_CHARS = " .+@#$%&*=-;:>,<1234567890qwertyuiopasdfghjklzxcvbnmQWERTYUIOPASDFGHJKLZXCVBNM!~^/()_`'|{}[]?";

  public static byte[] ToBytes(XpmFile file) => Encoding.UTF8.GetBytes(ToText(file));

  public static string ToText(XpmFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var sb = new StringBuilder();
    var charsPerPixel = file.CharsPerPixel;
    var numColors = file.PaletteColorCount;
    var charMappings = _GenerateCharMappings(numColors, charsPerPixel);

    sb.AppendLine("/* XPM */");
    sb.Append("static char *");
    sb.Append(file.Name);
    sb.AppendLine("[] = {");

    // Values line
    sb.Append('"');
    sb.Append(file.Width.ToString(CultureInfo.InvariantCulture));
    sb.Append(' ');
    sb.Append(file.Height.ToString(CultureInfo.InvariantCulture));
    sb.Append(' ');
    sb.Append(numColors.ToString(CultureInfo.InvariantCulture));
    sb.Append(' ');
    sb.Append(charsPerPixel.ToString(CultureInfo.InvariantCulture));
    sb.AppendLine("\",");

    // Color definitions
    for (var i = 0; i < numColors; ++i) {
      sb.Append('"');
      sb.Append(charMappings[i]);
      sb.Append("\tc ");
      if (file.TransparentIndex.HasValue && file.TransparentIndex.Value == i) {
        sb.Append("None");
      } else {
        var r = file.Palette[i * 3];
        var g = file.Palette[i * 3 + 1];
        var b = file.Palette[i * 3 + 2];
        sb.Append('#');
        sb.Append(r.ToString("X2", CultureInfo.InvariantCulture));
        sb.Append(g.ToString("X2", CultureInfo.InvariantCulture));
        sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
      }

      sb.Append('"');
      sb.AppendLine(",");
    }

    // Pixel rows
    for (var y = 0; y < file.Height; ++y) {
      sb.Append('"');
      for (var x = 0; x < file.Width; ++x) {
        var index = file.PixelData[y * file.Width + x];
        sb.Append(charMappings[index]);
      }

      sb.Append('"');
      if (y < file.Height - 1)
        sb.AppendLine(",");
      else
        sb.AppendLine();
    }

    sb.AppendLine("};");

    return sb.ToString();
  }

  private static string[] _GenerateCharMappings(int numColors, int charsPerPixel) {
    var mappings = new string[numColors];

    if (charsPerPixel == 1) {
      for (var i = 0; i < numColors; ++i) {
        if (i < _DEFAULT_CHARS.Length)
          mappings[i] = _DEFAULT_CHARS[i].ToString();
        else
          mappings[i] = ((char)('!' + i)).ToString();
      }
    } else {
      var baseLen = _DEFAULT_CHARS.Length;
      for (var i = 0; i < numColors; ++i) {
        var chars = new char[charsPerPixel];
        var remaining = i;
        for (var c = charsPerPixel - 1; c >= 0; --c) {
          chars[c] = _DEFAULT_CHARS[remaining % baseLen];
          remaining /= baseLen;
        }

        mappings[i] = new string(chars);
      }
    }

    return mappings;
  }
}
