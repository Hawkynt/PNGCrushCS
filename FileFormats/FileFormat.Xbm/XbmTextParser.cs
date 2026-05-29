using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace FileFormat.Xbm;

/// <summary>Parses XBM C source text to extract image data.</summary>
internal static class XbmTextParser {

  private static readonly Regex _DefineRegex = new(@"#define\s+(\w+)_(\w+)\s+(\d+)", RegexOptions.Compiled);
  private static readonly Regex _HexByteRegex = new(@"0[xX]([0-9a-fA-F]{1,2})", RegexOptions.Compiled);

  public static XbmFile Parse(string text) {
    ArgumentNullException.ThrowIfNull(text);

    int? width = null;
    int? height = null;
    int? hotspotX = null;
    int? hotspotY = null;
    string? name = null;

    var defineMatches = _DefineRegex.Matches(text);
    foreach (Match match in defineMatches) {
      var prefix = match.Groups[1].Value;
      var suffix = match.Groups[2].Value;
      var value = int.Parse(match.Groups[3].Value);

      name ??= prefix;

      switch (suffix) {
        case "width":
          width = value;
          break;
        case "height":
          height = value;
          break;
        case "hot" when suffix == "hot":
          // handled by x_hot and y_hot below
          break;
      }
    }

    // Handle x_hot and y_hot which have underscores in the suffix
    var hotspotXRegex = new Regex(@"#define\s+(\w+)_x_hot\s+(\d+)", RegexOptions.Compiled);
    var hotspotYRegex = new Regex(@"#define\s+(\w+)_y_hot\s+(\d+)", RegexOptions.Compiled);

    var xHotMatch = hotspotXRegex.Match(text);
    if (xHotMatch.Success) {
      hotspotX = int.Parse(xHotMatch.Groups[2].Value);
      name ??= xHotMatch.Groups[1].Value;
    }

    var yHotMatch = hotspotYRegex.Match(text);
    if (yHotMatch.Success) {
      hotspotY = int.Parse(yHotMatch.Groups[2].Value);
      name ??= yHotMatch.Groups[1].Value;
    }

    if (width == null || height == null)
      throw new InvalidDataException("XBM file missing width or height #define.");

    name ??= "image";

    var hexMatches = _HexByteRegex.Matches(text);
    var bytes = new List<byte>(hexMatches.Count);
    foreach (Match match in hexMatches)
      bytes.Add(Convert.ToByte(match.Groups[1].Value, 16));

    var bytesPerRow = (width.Value + 7) / 8;
    var expectedBytes = bytesPerRow * height.Value;
    if (bytes.Count < expectedBytes)
      throw new InvalidDataException($"XBM file has {bytes.Count} data bytes but expected at least {expectedBytes}.");

    return new XbmFile {
      Width = width.Value,
      Height = height.Value,
      Name = name,
      HotspotX = hotspotX,
      HotspotY = hotspotY,
      PixelData = bytes.GetRange(0, expectedBytes).ToArray()
    };
  }
}
