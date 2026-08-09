using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FileFormat.Hpgl;

/// <summary>Splits an HP-GL plot into its instructions.</summary>
/// <remarks>
/// Two letters name the instruction and everything up to the next letter or semicolon is its
/// parameters — the terminator is optional almost everywhere, and the language's own reference says
/// any non-numeric character ends an instruction. The four that take characters rather than numbers
/// are the exceptions and are read by their own rules: a label runs to its terminator, which
/// another instruction can change; a comment runs to its closing quote.
/// </remarks>
public static class HpglReader {

  /// <summary>The escape a device-control sequence opens with.</summary>
  private const char _Escape = '';

  /// <summary>How many instructions a plot may hold, which bounds what a wrong guess can cost.</summary>
  private const int _MaxInstructions = 1 << 22;

  /// <summary>
  /// Whether a byte can stand between instructions, where the language is printable ASCII and
  /// nothing else.
  /// </summary>
  /// <remarks>
  /// HP-GL is a text language. The bytes outside this set that a plot may legitimately carry all sit
  /// inside something delimited — a label, a comment, or the printable-character alphabet of the
  /// Polyline Encoded instruction — and each of those is consumed whole by the parse, so nothing
  /// belonging to one reaches this test.
  /// </remarks>
  private static bool _IsPlotText(char c)
    => c is >= ' ' and <= '~' || c is '\t' or '\n' or '\r' or '\f' or _Escape;

  public static HpglFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("HP-GL plot not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HpglFile FromStream(Stream stream) {
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

  public static HpglFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static HpglFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 4)
      throw new InvalidDataException("An HP-GL plot is too short to hold a single instruction.");

    var text = Encoding.Latin1.GetString(data);
    var instructions = _Parse(text);

    // A plot is nothing but instructions, and any two letters in a row look like one — the sentence
    // "jumps over the lazy dog" yields ER, which is an instruction that draws. So what is required
    // is an instruction that moves the pen and states where to, which prose does not produce.
    var drawing = 0;
    foreach (var instruction in instructions)
      if (instruction.Numbers.Length >= 2 && instruction.Mnemonic is "PU" or "PD" or "PA" or "PR" or "AA" or "AR" or "EA" or "ER" or "RA" or "RR")
        ++drawing;
      else if (instruction.Mnemonic is "CI" && instruction.Numbers.Length >= 1)
        ++drawing;

    if (drawing == 0)
      throw new InvalidDataException("Not an HP-GL plot: nothing in it moves the pen to anywhere.");

    return new() { Instructions = instructions };
  }

  private static List<HpglInstruction> _Parse(string text) {
    var instructions = new List<HpglInstruction>();
    var terminator = '';
    var at = 0;

    while (at < text.Length && instructions.Count < _MaxInstructions) {
      var c = text[at];

      if (c == _Escape) {
        at = _SkipEscape(text, at);
        continue;
      }

      // A byte that cannot stand in the language ends the plot. Without this the parse reads any
      // file at all, and a run of compressed bytes long enough will sooner or later carry two
      // letters and a pair of numbers — which is all the test in FromSpan asks for. A PNG under the
      // name this format claims from a printer spool was drawn as a picture three pixels square.
      //
      // Stopping rather than refusing outright is what keeps a real plot readable: a spool padded
      // out to a block boundary, or a job that switches to a raster language partway, still yields
      // everything drawn up to that point. A file that was never a plot yields nothing to draw and
      // is refused by the test that follows.
      if (!_IsPlotText(c))
        break;

      if (!char.IsAsciiLetter(c)) {
        ++at;
        continue;
      }

      if (at + 1 >= text.Length)
        break;

      if (!char.IsAsciiLetter(text[at + 1])) {
        ++at;
        continue;
      }

      var mnemonic = text.Substring(at, 2).ToUpperInvariant();
      at += 2;

      switch (mnemonic) {
        case "LB": {
          var end = text.IndexOf(terminator, at);
          if (end < 0)
            end = text.Length;

          instructions.Add(new(mnemonic, [], text[at..end]));
          at = end < text.Length ? end + 1 : end;
          continue;
        }

        case "DT": {
          // The character straight after names the terminator, and a mode may follow it.
          if (at < text.Length && text[at] != ';')
            terminator = text[at++];

          var end = text.IndexOf(';', at);
          at = end < 0 ? text.Length : end + 1;
          instructions.Add(new(mnemonic, [], string.Empty));
          continue;
        }

        case "CO": {
          var open = text.IndexOf('"', at);
          var close = open < 0 ? -1 : text.IndexOf('"', open + 1);
          at = close < 0 ? text.Length : close + 1;
          continue;
        }

        case "PE": {
          // Polyline Encoded packs its coordinates into printable characters and must end with a
          // semicolon. It is not decoded here, but it has to be skipped as a whole or its bytes
          // would be read as instructions.
          var end = text.IndexOf(';', at);
          at = end < 0 ? text.Length : end + 1;
          instructions.Add(new(mnemonic, [], string.Empty));
          continue;
        }
      }

      var (numbers, next) = _Parameters(text, at);
      at = next;
      instructions.Add(new(mnemonic, numbers, string.Empty));
    }

    return instructions;
  }

  private static (double[] Numbers, int Next) _Parameters(string text, int at) {
    var numbers = new List<double>();

    while (at < text.Length) {
      var c = text[at];
      if (c == ';') {
        ++at;
        break;
      }

      if (char.IsWhiteSpace(c) || c == ',') {
        ++at;
        continue;
      }

      if (!char.IsAsciiDigit(c) && c is not ('-' or '+' or '.'))
        break;

      var start = at;
      if (c is '-' or '+')
        ++at;

      while (at < text.Length && char.IsAsciiDigit(text[at]))
        ++at;

      if (at < text.Length && text[at] == '.') {
        ++at;
        while (at < text.Length && char.IsAsciiDigit(text[at]))
          ++at;
      }

      if (at == start) {
        ++at;
        continue;
      }

      if (double.TryParse(text.AsSpan(start, at - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        numbers.Add(value);
    }

    return (numbers.ToArray(), at);
  }

  /// <summary>
  /// Steps over a device-control or context-switching sequence, which carry no geometry.
  /// </summary>
  /// <remarks>
  /// Two families. <c>ESC .</c> and a letter is a plotter instruction whose parameters are ended by
  /// a colon; <c>ESC %</c> and a number and a letter switches between the printer's language and
  /// this one. Both have to be recognised, or their bytes would be read as mnemonics.
  /// </remarks>
  private static int _SkipEscape(string text, int at) {
    ++at;
    if (at >= text.Length)
      return at;

    if (text[at] == '.') {
      ++at;
      if (at < text.Length)
        ++at;

      while (at < text.Length && text[at] != ':' && text[at] != _Escape)
        ++at;

      return at < text.Length && text[at] == ':' ? at + 1 : at;
    }

    if (text[at] is '%' or '&' or '(' or ')' or '*') {
      ++at;
      while (at < text.Length && !char.IsAsciiLetterUpper(text[at]))
        ++at;

      return at < text.Length ? at + 1 : at;
    }

    return at + 1;
  }
}
