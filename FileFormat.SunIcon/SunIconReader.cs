using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FileFormat.SunIcon;

/// <summary>Reads Sun Icon files from bytes, streams, or file paths.</summary>
public static class SunIconReader {

  private const int _MINIMUM_SIZE = 10;
  private const string _MAGIC = "/* ";

  private static readonly Regex _HeaderFieldRegex = new(
    @"(\w+)\s*=\s*(\d+)",
    RegexOptions.Compiled
  );

  private static readonly Regex _HexValueRegex = new(
    @"0[xX]([0-9a-fA-F]+)",
    RegexOptions.Compiled
  );

  public static SunIconFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Sun Icon file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SunIconFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static SunIconFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static SunIconFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MINIMUM_SIZE)
      throw new InvalidDataException("Data too small for a valid Sun Icon file.");

    var text = Encoding.ASCII.GetString(data);

    if (!text.StartsWith(_MAGIC, StringComparison.Ordinal))
      throw new InvalidDataException("Invalid Sun Icon format: missing '/* ' magic.");

    return _Parse(text);
  }

  private static SunIconFile _Parse(string text) {
    // Extract the C comment header block
    var commentEnd = text.IndexOf("*/", StringComparison.Ordinal);
    if (commentEnd < 0)
      throw new InvalidDataException("Invalid Sun Icon format: unterminated comment header.");

    var header = text[..commentEnd];

    int? width = null;
    int? height = null;
    int? depth = null;
    var validBitsPerItem = 16;

    var matches = _HeaderFieldRegex.Matches(header);
    foreach (Match match in matches) {
      var key = match.Groups[1].Value;
      var value = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);

      switch (key) {
        case "Width":
          width = value;
          break;
        case "Height":
          height = value;
          break;
        case "Depth":
          depth = value;
          break;
        case "Valid_bits_per_item":
          validBitsPerItem = value;
          break;
      }
    }

    if (width is null or <= 0)
      throw new InvalidDataException("Invalid Sun Icon format: missing or invalid Width.");
    if (height is null or <= 0)
      throw new InvalidDataException("Invalid Sun Icon format: missing or invalid Height.");
    if (depth is not null and not 1)
      throw new InvalidDataException($"Unsupported Sun Icon depth: {depth}. Only depth=1 is supported.");
    if (validBitsPerItem is not 16 and not 32)
      throw new InvalidDataException($"Unsupported Valid_bits_per_item: {validBitsPerItem}. Only 16 and 32 are supported.");

    // Parse hex values from the data section (after the comment)
    var dataSection = text[(commentEnd + 2)..];
    var hexMatches = _HexValueRegex.Matches(dataSection);
    var items = new List<uint>(hexMatches.Count);
    foreach (Match match in hexMatches)
      items.Add(uint.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    // Convert items to packed 1bpp row data
    var bytesPerRow = (width.Value + 7) / 8;
    var totalPixelBytes = bytesPerRow * height.Value;
    var pixelData = new byte[totalPixelBytes];

    var bitsPerItem = validBitsPerItem;
    var bytesPerItem = bitsPerItem / 8;
    var pixelIndex = 0;

    foreach (var item in items) {
      // Items are MSB-first, write high byte first
      for (var b = bytesPerItem - 1; b >= 0; --b) {
        if (pixelIndex >= totalPixelBytes)
          break;

        pixelData[pixelIndex] = (byte)(item >> (b * 8));
        ++pixelIndex;
      }

      if (pixelIndex >= totalPixelBytes)
        break;
    }

    return new SunIconFile {
      Width = width.Value,
      Height = height.Value,
      PixelData = pixelData,
    };
  }
}
