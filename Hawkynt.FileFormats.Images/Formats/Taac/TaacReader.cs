using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FileFormat.Taac;

/// <summary>Reads a Sun TAAC bitmap out of its text header and the raster after it.</summary>
/// <remarks>
/// The header is <c>name=value;</c> lines. A value runs to its semicolon, a backslash escapes the
/// character after it, and the whole header ends at a form feed with a newline after it. What is
/// checked here is that the header says the same thing twice over wherever it can: <c>size</c> gives
/// as many extents as <c>rank</c> claims dimensions, <c>colormapsize</c> matches the number of
/// entries <c>colormap</c> actually carries, and the bytes after the form feed are as many as the
/// stated size, bands and sample width need.
/// </remarks>
public static class TaacReader {

  /// <summary>How long the text header may be, which bounds what a file with no form feed costs.</summary>
  private const int _MaxHeaderLength = 1 << 20;

  /// <summary>The most pixels a picture may claim.</summary>
  private const long _MaxPixels = 1L << 28;

  /// <summary>The most entries a colour map may carry, one byte of index being all that reaches it.</summary>
  private const int _MaxPaletteEntries = 256;

  public static TaacFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("TAAC bitmap not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static TaacFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromBytes(memory.ToArray());
  }

  public static TaacFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static TaacFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 8)
      throw new InvalidDataException("A TAAC bitmap is too short to hold a header.");

    if (!data[..4].SequenceEqual(Encoding.ASCII.GetBytes(TaacFile.Magic)))
      throw new InvalidDataException("Not a TAAC bitmap: it does not open with \"ncaa\".");

    var terminator = data.IndexOf(TaacFile.HeaderTerminator);
    if (terminator < 0)
      throw new InvalidDataException("A TAAC bitmap's header ends at a form feed, and this one has none.");

    if (terminator > _MaxHeaderLength)
      throw new InvalidDataException($"A header of {terminator} bytes is longer than a TAAC bitmap's.");

    var fields = _Fields(Encoding.Latin1.GetString(data[4..terminator]));

    // The form feed is followed by the newline that ended its line, and the picture starts after it.
    var start = terminator + 1;
    if (start < data.Length && data[start] == '\n')
      ++start;

    var rank = _Integer(fields, "rank", 2);
    if (rank != 2)
      throw new InvalidDataException($"A TAAC file of rank {rank} is a {rank}-dimensional volume rather than a picture.");

    var type = _Text(fields, "type");
    if (type != null && !type.Equals("raster", StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException($"A TAAC file of type \"{type}\" holds something other than a raster.");

    var size = _Numbers(fields, "size");
    if (size.Count != rank)
      throw new InvalidDataException($"A TAAC bitmap of rank {rank} states {size.Count} extents rather than {rank}.");

    var width = size[0];
    var height = size[1];
    if (width < 1 || height < 1 || (long)width * height > _MaxPixels)
      throw new InvalidDataException($"A TAAC bitmap of {width} by {height} cannot be read.");

    var bits = _Integer(fields, "bits", 8);
    if (bits != 8)
      throw new InvalidDataException($"A TAAC bitmap of {bits} bits a sample is not one of the eight-bit rasters the board displayed.");

    var bands = _Integer(fields, "bands", 1);
    if (bands is not (1 or 3))
      throw new InvalidDataException($"A TAAC bitmap of {bands} bands is neither the single band nor the three this reads.");

    var (palette, count) = _Colours(fields);

    // The header states how big the picture is, so the file has to carry that many bytes. One that
    // does not has been cut, and reading what is there would draw part of a picture as a whole one.
    var needed = (long)width * height * bands;
    var available = data.Length - start;
    if (available < needed)
      throw new InvalidDataException($"A TAAC bitmap of {width} by {height} in {bands} band(s) needs {needed} bytes and the file has {available}.");

    return new() {
      Width = width,
      Height = height,
      Bands = bands,
      PixelData = data.Slice(start, (int)needed).ToArray(),
      Palette = palette,
      PaletteCount = count
    };
  }

  /// <summary>
  /// Splits the header into its named values.
  /// </summary>
  /// <remarks>
  /// A field is a name, an equals sign, a value and a semicolon. A backslash escapes whatever comes
  /// after it, which is how a value carries a semicolon of its own. Running off the end of the
  /// header before the semicolon means the field was never finished.
  /// </remarks>
  private static Dictionary<string, string> _Fields(string header) {
    var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var at = 0;

    while (at < header.Length) {
      while (at < header.Length && (char.IsWhiteSpace(header[at]) || header[at] == ';'))
        ++at;

      if (at >= header.Length)
        break;

      var nameStart = at;
      while (at < header.Length && header[at] is not ('=' or ';') && !char.IsWhiteSpace(header[at]))
        ++at;

      var name = header[nameStart..at];
      while (at < header.Length && char.IsWhiteSpace(header[at]))
        ++at;

      if (at >= header.Length || header[at] != '=') {
        // A word with no value is a comment line rather than a field; skipping to the next
        // semicolon leaves the fields around it readable.
        while (at < header.Length && header[at] != ';')
          ++at;

        continue;
      }

      ++at;
      var value = new StringBuilder();
      var closed = false;
      while (at < header.Length) {
        var c = header[at++];
        if (c == '\\') {
          if (at < header.Length)
            value.Append(header[at++]);

          continue;
        }

        if (c == ';') {
          closed = true;
          break;
        }

        value.Append(c);
      }

      if (!closed)
        throw new InvalidDataException($"The header field \"{name}\" runs off the end without its semicolon.");

      if (name.Length > 0)
        fields[name] = value.ToString().Trim();
    }

    return fields;
  }

  private static string? _Text(Dictionary<string, string> fields, string name)
    => fields.GetValueOrDefault(name);

  private static int _Integer(Dictionary<string, string> fields, string name, int fallback) {
    var text = _Text(fields, name);
    if (text == null)
      return fallback;

    if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
      throw new InvalidDataException($"The header field \"{name}\" is \"{text}\", which is not a whole number.");

    return value;
  }

  private static List<int> _Numbers(Dictionary<string, string> fields, string name) {
    var text = _Text(fields, name)
      ?? throw new InvalidDataException($"A TAAC bitmap states its extents in \"{name}\", and this one has no such field.");

    var numbers = new List<int>();
    foreach (var part in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)) {
      if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        throw new InvalidDataException($"The header field \"{name}\" holds \"{part}\", which is not a whole number.");

      numbers.Add(value);
    }

    return numbers;
  }

  /// <summary>
  /// Reads the colour map, whose entries are six hexadecimal digits standing for blue, green and
  /// red in that order.
  /// </summary>
  private static (byte[]? Palette, int Count) _Colours(Dictionary<string, string> fields) {
    var text = _Text(fields, "colormap");
    if (text == null)
      return (null, 0);

    var entries = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    if (entries.Length > _MaxPaletteEntries)
      throw new InvalidDataException($"A colour map of {entries.Length} entries is more than an eight-bit index reaches.");

    var palette = new byte[entries.Length * 3];
    for (var i = 0; i < entries.Length; ++i) {
      var entry = entries[i];
      if (entry.Length != 6)
        throw new InvalidDataException($"Colour map entry {i} is \"{entry}\", which is not six hexadecimal digits.");

      if (!byte.TryParse(entry.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue)
        || !byte.TryParse(entry.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green)
        || !byte.TryParse(entry.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red))
        throw new InvalidDataException($"Colour map entry {i} is \"{entry}\", which is not six hexadecimal digits.");

      palette[i * 3] = red;
      palette[i * 3 + 1] = green;
      palette[i * 3 + 2] = blue;
    }

    // The header says how many entries the map has as well as listing them, and the two disagreeing
    // means one of them is not what the file was written with.
    var stated = _Integer(fields, "colormapsize", entries.Length);
    if (stated != entries.Length)
      throw new InvalidDataException($"The header states a colour map of {stated} entries and carries {entries.Length}.");

    return (palette, entries.Length);
  }
}
