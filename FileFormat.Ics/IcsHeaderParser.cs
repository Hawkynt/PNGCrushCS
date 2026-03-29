using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.Ics;

/// <summary>Parsed ICS header containing image dimensions, format, and compression information.</summary>
internal sealed record IcsHeader {
  public required string Version { get; init; }
  public required int Width { get; init; }
  public required int Height { get; init; }
  public required int Channels { get; init; }
  public required int BitsPerSample { get; init; }
  public required IcsCompression Compression { get; init; }
  public required int DataOffset { get; init; }
  public required string[] DimensionOrder { get; init; }
  public required string RepresentationFormat { get; init; }
}

/// <summary>Parses and formats ICS text headers.</summary>
internal static class IcsHeaderParser {

  public static IcsHeader Parse(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    var text = Encoding.ASCII.GetString(data);
    var offset = 0;
    var fields = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
    string? version = null;
    var foundEnd = false;

    while (offset < text.Length) {
      var lineEnd = text.IndexOf('\n', offset);
      if (lineEnd < 0)
        lineEnd = text.Length;

      var line = text.Substring(offset, lineEnd - offset).TrimEnd('\r');
      offset = lineEnd + 1;

      if (line.Equals("end", StringComparison.OrdinalIgnoreCase)) {
        foundEnd = true;
        break;
      }

      var parts = line.Split('\t');
      if (parts.Length >= 2 && parts[0].Equals("ics_version", StringComparison.OrdinalIgnoreCase)) {
        version = parts[1].Trim();
        continue;
      }

      if (parts.Length >= 3) {
        var key = parts[0].Trim() + "\t" + parts[1].Trim();
        var values = new string[parts.Length - 2];
        for (var i = 2; i < parts.Length; ++i)
          values[i - 2] = parts[i].Trim();
        fields[key] = values;
      }
    }

    if (!foundEnd)
      throw new InvalidDataException("ICS header terminator 'end' not found.");

    version ??= "2.0";
    if (version != "1.0" && version != "2.0")
      throw new InvalidDataException($"Unsupported ICS version: {version}.");

    // Parse layout order
    string[] dimensionOrder;
    if (!fields.TryGetValue("layout\torder", out var orderValues))
      throw new InvalidDataException("ICS header missing 'layout order' field.");
    dimensionOrder = orderValues;

    // Parse layout sizes
    if (!fields.TryGetValue("layout\tsizes", out var sizeValues))
      throw new InvalidDataException("ICS header missing 'layout sizes' field.");

    if (sizeValues.Length != dimensionOrder.Length)
      throw new InvalidDataException("ICS 'layout sizes' count does not match 'layout order' count.");

    var sizes = new int[sizeValues.Length];
    for (var i = 0; i < sizeValues.Length; ++i)
      if (!int.TryParse(sizeValues[i], out sizes[i]))
        throw new InvalidDataException($"Invalid size value: '{sizeValues[i]}'.");

    // Extract dimensions
    var width = 0;
    var height = 0;
    var channels = 1;
    var bitsPerSample = 8;

    for (var i = 0; i < dimensionOrder.Length; ++i) {
      var dim = dimensionOrder[i].ToLowerInvariant();
      switch (dim) {
        case "x":
          width = sizes[i];
          break;
        case "y":
          height = sizes[i];
          break;
        case "ch":
          channels = sizes[i];
          break;
        case "bits":
          bitsPerSample = sizes[i];
          break;
      }
    }

    // Parse significant bits (overrides bits dimension if present)
    if (fields.TryGetValue("layout\tsignificant_bits", out var sigBitsValues) && sigBitsValues.Length > 0)
      if (int.TryParse(sigBitsValues[0], out var sigBits))
        bitsPerSample = sigBits;

    // Parse representation format
    var representationFormat = "integer";
    if (fields.TryGetValue("representation\tformat", out var formatValues) && formatValues.Length > 0)
      representationFormat = formatValues[0].ToLowerInvariant();

    // Parse compression
    var compression = IcsCompression.Uncompressed;
    if (fields.TryGetValue("representation\tcompression", out var compValues) && compValues.Length > 0)
      if (compValues[0].Equals("gzip", StringComparison.OrdinalIgnoreCase))
        compression = IcsCompression.Gzip;

    return new IcsHeader {
      Version = version,
      Width = width,
      Height = height,
      Channels = channels,
      BitsPerSample = bitsPerSample,
      Compression = compression,
      DataOffset = _GetByteOffset(data, offset),
      DimensionOrder = dimensionOrder,
      RepresentationFormat = representationFormat,
    };
  }

  public static byte[] Format(IcsFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var sb = new StringBuilder();
    sb.Append("ics_version\t").Append(file.Version).Append('\n');

    // Determine dimension order and sizes
    if (file.Channels > 1) {
      sb.Append("layout\tparameters\t4\n");
      sb.Append("layout\torder\tbits\tx\ty\tch\n");
      sb.Append("layout\tsizes\t").Append(file.BitsPerSample)
        .Append('\t').Append(file.Width)
        .Append('\t').Append(file.Height)
        .Append('\t').Append(file.Channels)
        .Append('\n');
    } else {
      sb.Append("layout\tparameters\t3\n");
      sb.Append("layout\torder\tbits\tx\ty\n");
      sb.Append("layout\tsizes\t").Append(file.BitsPerSample)
        .Append('\t').Append(file.Width)
        .Append('\t').Append(file.Height)
        .Append('\n');
    }

    sb.Append("layout\tsignificant_bits\t").Append(file.BitsPerSample).Append('\n');
    sb.Append("representation\tformat\tinteger\n");
    sb.Append("representation\tcompression\t")
      .Append(file.Compression == IcsCompression.Gzip ? "gzip" : "uncompressed")
      .Append('\n');
    sb.Append("representation\tbyte_order\t1 2 3 4\n");
    sb.Append("end\n");

    return Encoding.ASCII.GetBytes(sb.ToString());
  }

  /// <summary>Converts a character offset to a byte offset, accounting for the ASCII encoding.</summary>
  private static int _GetByteOffset(byte[] data, int charOffset) => Math.Min(charOffset, data.Length);
}
