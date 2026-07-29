using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FileFormat.XbmColor;

public static partial class XbmColorReader {

  public static XbmColorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Color XBM file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static XbmColorFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static XbmColorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static XbmColorFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 32)
      throw new InvalidDataException("Color XBM data too small.");
    var text = Encoding.ASCII.GetString(data);
    if (!text.Contains("_colors") || !text.Contains("_palette[]"))
      throw new InvalidDataException("Color XBM marker (_colors / _palette[]) missing — not a Color XBM file.");

    var width = _ReadDefine(text, "width");
    var height = _ReadDefine(text, "height");
    var colors = _ReadDefine(text, "colors");
    if (width <= 0 || height <= 0 || colors is <= 0 or > 256)
      throw new InvalidDataException($"Color XBM has implausible header: {width}x{height} colors={colors}.");

    var palette = _ReadByteArray(text, "palette");
    var pixels = _ReadByteArray(text, "pixels");
    if (palette.Length < colors * 3)
      throw new InvalidDataException($"Color XBM palette underflow: have {palette.Length} bytes for {colors} colours (need {colors * 3}).");
    if (pixels.Length < width * height)
      throw new InvalidDataException($"Color XBM pixel underflow: have {pixels.Length} bytes for {width}x{height} (need {width * height}).");

    return new XbmColorFile {
      Width = width,
      Height = height,
      Name = _ExtractName(text) ?? "image",
      Palette = palette,
      ColorCount = colors,
      PixelData = pixels,
    };
  }

  private static int _ReadDefine(string text, string suffix) {
    var m = _DefineRegex().Matches(text);
    foreach (Match match in m) {
      if (match.Groups["suffix"].Value == suffix)
        return int.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
    }
    throw new InvalidDataException($"Missing '#define ..._{suffix} N' line.");
  }

  private static byte[] _ReadByteArray(string text, string suffix) {
    var m = _ArrayRegex(suffix).Match(text);
    if (!m.Success)
      throw new InvalidDataException($"Missing 'static unsigned char ..._{suffix}[] = {{ ... }}' block.");
    var body = m.Groups["body"].Value;
    var bytes = new List<byte>();
    foreach (var token in body.Split([',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)) {
      var raw = token.Trim();
      if (raw.Length == 0) continue;
      int v;
      if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        v = int.Parse(raw[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
      else
        v = int.Parse(raw, CultureInfo.InvariantCulture);
      bytes.Add((byte)v);
    }
    return bytes.ToArray();
  }

  private static string? _ExtractName(string text) {
    var m = _NameRegex().Match(text);
    return m.Success ? m.Groups["name"].Value : null;
  }

  [GeneratedRegex(@"#define\s+(?<name>\w+)_(?<suffix>\w+)\s+(?<value>\d+)")]
  private static partial Regex _DefineRegex();

  [GeneratedRegex(@"#define\s+(?<name>\w+)_width")]
  private static partial Regex _NameRegex();

  private static Regex _ArrayRegex(string suffix)
    => new(@"static\s+unsigned\s+char\s+\w+_" + Regex.Escape(suffix) + @"\[\]\s*=\s*\{(?<body>[^}]*)\}",
           RegexOptions.Singleline);
}
