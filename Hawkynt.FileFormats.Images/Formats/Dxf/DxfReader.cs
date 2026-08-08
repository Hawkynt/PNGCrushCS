using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FileFormat.Dxf;

/// <summary>Splits a Drawing Exchange File into its group code and value pairs.</summary>
/// <remarks>
/// Autodesk's <em>About the General DXF File Structure</em>: a DXF file is composed of pairs of
/// codes and associated values, written one to a line, and it is organised into sections that open
/// with a 0 group whose value is <c>SECTION</c> followed by a 2 group naming the section, and close
/// with a 0 group whose value is <c>ENDSEC</c>. The file ends with a 0 group whose value is
/// <c>EOF</c>.
/// <para/>
/// That structure is what identifies the file, because a DXF file has no magic number: it is text,
/// and the only thing separating it from any other text is that every second line parses as an
/// integer and the sections open and close in order. So the shape is checked rather than sniffed —
/// a code with no value after it, a section opened inside another, an <c>ENDSEC</c> with nothing
/// open, or a missing <c>EOF</c> all refuse the file rather than being read past.
/// </remarks>
public static class DxfReader {

  /// <summary>How many pairs a drawing may hold, which bounds what a wrong guess can cost.</summary>
  private const int _MaxPairs = 1 << 24;

  /// <summary>The largest group code the reference defines, which is 1071.</summary>
  private const int _MaxGroupCode = 1071;

  /// <summary>The smallest, which is the -5 of the application-use codes.</summary>
  private const int _MinGroupCode = -5;

  public static DxfFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Drawing exchange file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DxfFile FromStream(Stream stream) {
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

  public static DxfFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static DxfFile FromSpan(ReadOnlySpan<byte> data) {
    // "  0\nSECTION\n  2\nHEADER\n" is already twenty-four characters, and nothing shorter can
    // hold a section at all.
    if (data.Length < 16)
      throw new InvalidDataException("A drawing exchange file is too short to hold a section.");

    var text = Encoding.Latin1.GetString(data);
    if (text.StartsWith(DxfFile.BinarySentinel, StringComparison.Ordinal))
      throw new InvalidDataException("This is a binary DXF file, whose group codes are not text; only the ASCII form is read here.");

    var pairs = _Pairs(text);
    _CheckStructure(pairs);

    return new() { Pairs = pairs };
  }

  /// <summary>Reads the file as alternating code and value lines.</summary>
  private static List<DxfPair> _Pairs(string text) {
    var pairs = new List<DxfPair>();
    var at = 0;

    while (at < text.Length) {
      var code = _Line(text, ref at, out var sawLine);
      if (!sawLine)
        break;

      var trimmed = code.Trim();

      // A trailing newline leaves one empty line, which is the end rather than a broken pair.
      if (trimmed.Length == 0) {
        if (_Remaining(text, at))
          throw new InvalidDataException($"A blank line where group code {pairs.Count + 1} was expected: this is not a drawing exchange file.");

        break;
      }

      if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        throw new InvalidDataException($"\"{_Excerpt(trimmed)}\" is not a group code, so this is not a drawing exchange file.");

      if (number is < _MinGroupCode or > _MaxGroupCode)
        throw new InvalidDataException($"Group code {number} is outside the {_MinGroupCode} to {_MaxGroupCode} the reference defines.");

      var value = _Line(text, ref at, out var sawValue);
      if (!sawValue)
        throw new InvalidDataException($"Group code {number} is the last line in the file, with no value after it.");

      pairs.Add(new(number, value.TrimEnd()));
      if (pairs.Count > _MaxPairs)
        throw new InvalidDataException($"A drawing of more than {_MaxPairs} group codes is refused rather than read.");
    }

    if (pairs.Count == 0)
      throw new InvalidDataException("A drawing exchange file with no group codes at all.");

    return pairs;
  }

  /// <summary>Whether anything but line breaks and spaces is left.</summary>
  private static bool _Remaining(string text, int at) {
    for (var i = at; i < text.Length; ++i)
      if (!char.IsWhiteSpace(text[i]))
        return true;

    return false;
  }

  /// <summary>Takes one line, however it is ended.</summary>
  private static string _Line(string text, ref int at, out bool found) {
    if (at >= text.Length) {
      found = false;
      return string.Empty;
    }

    found = true;
    var start = at;
    while (at < text.Length && text[at] is not ('\n' or '\r'))
      ++at;

    var line = text[start..at];
    if (at < text.Length && text[at] == '\r')
      ++at;

    if (at < text.Length && text[at] == '\n')
      ++at;

    return line;
  }

  /// <summary>
  /// Checks the file is built the way the reference says, which is the only thing that tells a
  /// drawing from any other text.
  /// </summary>
  private static void _CheckStructure(List<DxfPair> pairs) {
    // 999 is a comment and may precede anything; the first thing that is not one has to open a
    // section, because that is what a DXF file starts with.
    var first = 0;
    while (first < pairs.Count && pairs[first].Code == 999)
      ++first;

    if (first >= pairs.Count || pairs[first].Code != 0 || pairs[first].Value != "SECTION")
      throw new InvalidDataException("A drawing exchange file opens with a SECTION, and this one does not.");

    var open = false;
    var sections = new List<string>();
    var ended = false;

    for (var i = first; i < pairs.Count; ++i) {
      var pair = pairs[i];
      if (pair.Code != 0)
        continue;

      switch (pair.Value) {
        case "SECTION": {
          if (open)
            throw new InvalidDataException($"A SECTION was opened inside the {sections[^1]} section, which the structure does not allow.");

          if (ended)
            throw new InvalidDataException("A SECTION follows the EOF that ends the file.");

          if (i + 1 >= pairs.Count || pairs[i + 1].Code != 2)
            throw new InvalidDataException("A SECTION with no name after it.");

          sections.Add(pairs[i + 1].Value);
          open = true;
          break;
        }

        case "ENDSEC": {
          if (!open)
            throw new InvalidDataException("An ENDSEC with no section open.");

          open = false;
          break;
        }

        case "EOF": {
          if (open)
            throw new InvalidDataException($"The file ends while the {sections[^1]} section is still open.");

          ended = true;
          break;
        }
      }
    }

    if (open)
      throw new InvalidDataException($"The {sections[^1]} section is never closed.");

    if (!ended)
      throw new InvalidDataException("A drawing exchange file ends with EOF, and this one never does.");

    if (!sections.Contains("ENTITIES"))
      throw new InvalidDataException("There is no ENTITIES section, so the file holds no drawing.");
  }

  private static string _Excerpt(string value) => value.Length <= 24 ? value : value[..24] + "...";
}
