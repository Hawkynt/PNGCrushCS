using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FileFormat.Nrrd;

/// <summary>Parses and formats NRRD text headers.</summary>
internal static class NrrdHeaderParser {

  public static Dictionary<string, string> Parse(string headerText) {
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var lines = headerText.Split('\n');

    for (var i = 0; i < lines.Length; ++i) {
      var line = lines[i].TrimEnd('\r');

      if (line.Length == 0)
        break;

      // Skip the magic line and comment lines
      if (line.StartsWith("NRRD", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal))
        continue;

      var colonIndex = line.IndexOf(": ", StringComparison.Ordinal);
      if (colonIndex < 0)
        continue;

      var key = line.Substring(0, colonIndex).Trim();
      var value = line.Substring(colonIndex + 2).Trim();
      result[key] = value;
    }

    return result;
  }

  public static string Format(NrrdFile file) {
    var sb = new StringBuilder();
    sb.Append("NRRD0004\n");
    sb.Append("type: ").Append(_TypeToString(file.DataType)).Append('\n');
    sb.Append("dimension: ").Append(file.Sizes.Length).Append('\n');
    sb.Append("sizes:").Append(_FormatIntArray(file.Sizes)).Append('\n');
    sb.Append("encoding: ").Append(_EncodingToString(file.Encoding)).Append('\n');
    sb.Append("endian: ").Append(file.Endian).Append('\n');

    if (file.Spacings.Length > 0)
      sb.Append("spacings:").Append(_FormatDoubleArray(file.Spacings)).Append('\n');

    if (file.Labels.Length > 0)
      sb.Append("labels:").Append(_FormatStringArray(file.Labels)).Append('\n');

    sb.Append('\n');
    return sb.ToString();
  }

  public static int FindDataOffset(byte[] data) {
    for (var i = 0; i < data.Length - 1; ++i)
      if (data[i] == (byte)'\n' && data[i + 1] == (byte)'\n')
        return i + 2;
      else if (i + 2 < data.Length && data[i] == (byte)'\r' && data[i + 1] == (byte)'\n' && i + 2 < data.Length && data[i + 2] == (byte)'\r')
        if (i + 3 < data.Length && data[i + 3] == (byte)'\n')
          return i + 4;

    throw new System.IO.InvalidDataException("No empty line found separating NRRD header from data.");
  }

  private static string _TypeToString(NrrdType type) => type switch {
    NrrdType.Int8 => "int8",
    NrrdType.UInt8 => "uint8",
    NrrdType.Int16 => "int16",
    NrrdType.UInt16 => "uint16",
    NrrdType.Int32 => "int32",
    NrrdType.UInt32 => "uint32",
    NrrdType.Float => "float",
    NrrdType.Double => "double",
    _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown NRRD type.")
  };

  internal static NrrdType ParseType(string value) => value.ToLowerInvariant() switch {
    "int8" or "signed char" => NrrdType.Int8,
    "uint8" or "unsigned char" or "uchar" => NrrdType.UInt8,
    "int16" or "short" or "short int" => NrrdType.Int16,
    "uint16" or "unsigned short" or "ushort" => NrrdType.UInt16,
    "int32" or "int" or "signed int" => NrrdType.Int32,
    "uint32" or "unsigned int" or "uint" => NrrdType.UInt32,
    "float" => NrrdType.Float,
    "double" => NrrdType.Double,
    _ => throw new System.IO.InvalidDataException($"Unknown NRRD type: '{value}'.")
  };

  internal static NrrdEncoding ParseEncoding(string value) => value.ToLowerInvariant() switch {
    "raw" => NrrdEncoding.Raw,
    "ascii" or "text" or "txt" => NrrdEncoding.Ascii,
    "hex" => NrrdEncoding.Hex,
    "gzip" or "gz" => NrrdEncoding.Gzip,
    _ => throw new System.IO.InvalidDataException($"Unknown NRRD encoding: '{value}'.")
  };

  internal static int[] ParseSizes(string value) {
    var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var sizes = new int[parts.Length];
    for (var i = 0; i < parts.Length; ++i)
      if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out sizes[i]))
        throw new System.IO.InvalidDataException($"Invalid size value: '{parts[i]}'.");

    return sizes;
  }

  internal static double[] ParseSpacings(string value) {
    var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var spacings = new double[parts.Length];
    for (var i = 0; i < parts.Length; ++i)
      if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out spacings[i]))
        throw new System.IO.InvalidDataException($"Invalid spacing value: '{parts[i]}'.");

    return spacings;
  }

  private static string _EncodingToString(NrrdEncoding encoding) => encoding switch {
    NrrdEncoding.Raw => "raw",
    NrrdEncoding.Ascii => "ascii",
    NrrdEncoding.Hex => "hex",
    NrrdEncoding.Gzip => "gzip",
    _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "Unknown NRRD encoding.")
  };

  private static string _FormatIntArray(int[] values) {
    var sb = new StringBuilder();
    for (var i = 0; i < values.Length; ++i)
      sb.Append(' ').Append(values[i].ToString(CultureInfo.InvariantCulture));

    return sb.ToString();
  }

  private static string _FormatDoubleArray(double[] values) {
    var sb = new StringBuilder();
    for (var i = 0; i < values.Length; ++i)
      sb.Append(' ').Append(values[i].ToString(CultureInfo.InvariantCulture));

    return sb.ToString();
  }

  private static string _FormatStringArray(string[] values) {
    var sb = new StringBuilder();
    for (var i = 0; i < values.Length; ++i)
      sb.Append(' ').Append('"').Append(values[i]).Append('"');

    return sb.ToString();
  }
}
