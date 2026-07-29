using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.AndrewToolkit;

/// <summary>Reads Andrew Toolkit (ATK) raster files from bytes, streams, or file paths.</summary>
public static class AndrewToolkitReader {

  private const int _MIN_FILE_SIZE = 8;

  public static AndrewToolkitFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ATK file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AndrewToolkitFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  public static AndrewToolkitFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _MIN_FILE_SIZE)
      throw new InvalidDataException($"Data too small for a valid ATK file: expected at least {_MIN_FILE_SIZE} bytes, got {data.Length}.");

    var headerLines = new List<string>();
    var offset = 0;
    var width = 0;
    var height = 0;

    while (offset < data.Length) {
      var lineEnd = data[offset..].IndexOf((byte)'\n');
      if (lineEnd < 0)
        break;

      lineEnd += offset;

      var line = Encoding.ASCII.GetString(data.Slice(offset, lineEnd - offset)).Trim();
      offset = lineEnd + 1;
      headerLines.Add(line);

      if (line.Length == 0)
        break;

      var lower = line.ToLowerInvariant();
      if (lower.Contains("width") && lower.Contains("="))
        int.TryParse(_ExtractValue(lower, "width"), out width);
      else if (lower.Contains("height") && lower.Contains("="))
        int.TryParse(_ExtractValue(lower, "height"), out height);
    }

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Could not parse valid dimensions from ATK header (width={width}, height={height}).");

    var remaining = data.Length - offset;
    var rawData = new byte[remaining];
    if (remaining > 0)
      data.Slice(offset, remaining).CopyTo(rawData.AsSpan(0));

    return new AndrewToolkitFile {
      Width = width,
      Height = height,
      RawData = rawData,
      HeaderLines = headerLines.ToArray(),
    };
  }

  public static AndrewToolkitFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static string _ExtractValue(string line, string keyword) {
    var idx = line.IndexOf(keyword, StringComparison.Ordinal);
    if (idx < 0)
      return string.Empty;

    var eqIdx = line.IndexOf('=', idx + keyword.Length);
    if (eqIdx < 0)
      return string.Empty;

    return line[(eqIdx + 1)..].Trim();
  }
}
