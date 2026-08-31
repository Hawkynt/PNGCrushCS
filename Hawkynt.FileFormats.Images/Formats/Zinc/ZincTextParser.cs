using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace FileFormat.Zinc;

/// <summary>Parses the textual USHORT array used by Zinc Interface Library bitmaps.</summary>
internal static class ZincTextParser {

  private static readonly Regex _ArrayRegex = new(
    @"^\s*USHORT\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\[\s*\]\s*=\s*\{(?<body>.*?)\}\s*;\s*$",
    RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

  private static readonly Regex _NumberRegex = new(
    @"0[xX][0-9A-Fa-f]{1,4}|[0-9]+",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);

  private static readonly Regex _SeparatorsRegex = new(
    @"^[\s,]*$",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);

  public static ZincFile Parse(string text) {
    ArgumentNullException.ThrowIfNull(text);

    var array = _ArrayRegex.Match(text);
    if (!array.Success)
      throw new InvalidDataException("Invalid Zinc bitmap: expected 'USHORT name[] = { ... };'.");

    var body = array.Groups["body"].Value;
    var numbers = _NumberRegex.Matches(body);
    if (numbers.Count < 2)
      throw new InvalidDataException("Zinc bitmap is missing width or height.");

    var remainder = _NumberRegex.Replace(body, string.Empty);
    if (!_SeparatorsRegex.IsMatch(remainder))
      throw new InvalidDataException("Zinc bitmap contains invalid array data.");

    var width = _ParseUShort(numbers[0].Value, "width");
    var height = _ParseUShort(numbers[1].Value, "height");
    if (width == 0 || height == 0)
      throw new InvalidDataException("Zinc bitmap dimensions must be positive.");

    var pixelCount = (long)width * height;
    if (pixelCount > ZincFile.MaximumPixels)
      throw new InvalidDataException($"Zinc bitmap exceeds the {ZincFile.MaximumPixels:N0}-pixel implementation safety limit.");

    var expectedWords = checked(ZincFile.GetWordsPerRow(width) * height);
    var actualWords = numbers.Count - 2;
    if (actualWords < expectedWords)
      throw new InvalidDataException($"Truncated Zinc raster: expected {expectedWords} words, got {actualWords}.");
    if (actualWords > expectedWords)
      throw new InvalidDataException($"Unexpected trailing Zinc raster data: expected {expectedWords} words, got {actualWords}.");

    var words = new ushort[expectedWords];
    for (var i = 0; i < words.Length; ++i)
      words[i] = _ParseUShort(numbers[i + 2].Value, $"raster word {i}");

    return new ZincFile {
      Width = width,
      Height = height,
      Name = array.Groups["name"].Value,
      RasterWords = words,
    };
  }

  private static ushort _ParseUShort(string token, string field) {
    if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
      if (ushort.TryParse(token.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var hexValue))
        return hexValue;
    } else if (ushort.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var decimalValue))
      return decimalValue;

    throw new InvalidDataException($"Zinc {field} value '{token}' is outside the USHORT range.");
  }
}
