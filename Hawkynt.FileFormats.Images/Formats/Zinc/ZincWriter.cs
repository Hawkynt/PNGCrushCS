using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FileFormat.Zinc;

/// <summary>Writes Zinc Interface Library bitmap source files.</summary>
public static class ZincWriter {

  private static readonly Regex _IdentifierRegex = new(
    @"^[A-Za-z_][A-Za-z0-9_]*$",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);

  public static byte[] ToBytes(ZincFile file) {
    ZincFile.Validate(file, nameof(file));

    var name = string.IsNullOrEmpty(file.Name) ? "image" : file.Name;
    if (!_IdentifierRegex.IsMatch(name))
      throw new ArgumentException("Zinc bitmap name must be a valid C identifier.", nameof(file));

    var sb = new StringBuilder();
    sb.Append("USHORT ").Append(name).Append("[] = {\n");
    sb.Append("  ").Append(file.Width.ToString(CultureInfo.InvariantCulture)).Append('\n');
    sb.Append("  ").Append(file.Height.ToString(CultureInfo.InvariantCulture)).Append('\n');

    var itemsOnLine = 0;
    for (var i = 0; i < file.RasterWords.Length; ++i) {
      if (i != 0)
        sb.Append(',');

      if (itemsOnLine == 11) {
        sb.Append('\n');
        itemsOnLine = 0;
      }

      if (itemsOnLine == 0)
        sb.Append(' ');

      ++itemsOnLine;
      sb.Append("0x").Append(file.RasterWords[i].ToString("x4", CultureInfo.InvariantCulture));
    }

    sb.Append("};\n");
    return Encoding.ASCII.GetBytes(sb.ToString());
  }

  public static void ToStream(ZincFile file, Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    stream.Write(ToBytes(file));
  }

  public static void ToFile(ZincFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
