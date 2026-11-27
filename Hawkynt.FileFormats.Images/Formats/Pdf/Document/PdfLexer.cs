using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FileFormat.Pdf.Document;

/// <summary>
/// Reads PDF objects out of bytes: the one tokenizer behind the file structure, object streams and
/// content streams alike.
/// </summary>
/// <remarks>
/// The syntax is that of PDF 32000-1 §7.2–7.3. Where real files break it the lexer bends the way
/// viewers do rather than failing: an unbalanced dictionary ends at the end of the data, a stray
/// byte that starts nothing is skipped, and a number with two signs or two points is read as far as
/// it makes sense. Each object read is returned in the shapes the document layer uses — names as
/// strings, strings as byte arrays, arrays as lists, dictionaries as <see cref="PdfDict"/>,
/// references as <see cref="PdfRef"/> — and any other bare word as a <see cref="PdfOperator"/>.
/// </remarks>
internal sealed class PdfLexer(byte[] data, int start = 0, int end = -1) {

  private readonly byte[] _data = data;
  private readonly int _end = end < 0 ? data.Length : end;

  /// <summary>How deep arrays and dictionaries may nest before the data is taken to be hostile.</summary>
  private const int _MaxDepth = 256;

  /// <summary>Where the next token starts.</summary>
  public int Position { get; set; } = start;

  /// <summary>The bytes being read.</summary>
  public byte[] Data => this._data;

  /// <summary>Where reading stops.</summary>
  public int End => this._end;

  /// <summary>Whether everything has been read.</summary>
  public bool AtEnd {
    get {
      this.SkipWhitespace();
      return this.Position >= this._end;
    }
  }

  /// <summary>Whether a byte is PDF white space.</summary>
  public static bool IsWhitespace(byte b) => b is 0 or 9 or 10 or 12 or 13 or 32;

  /// <summary>Whether a byte ends a token.</summary>
  public static bool IsDelimiter(byte b) => b is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';

  /// <summary>Skips white space and comments.</summary>
  public void SkipWhitespace() {
    var data = this._data;
    var pos = this.Position;
    while (pos < this._end) {
      var b = data[pos];
      if (IsWhitespace(b)) {
        ++pos;
        continue;
      }

      if (b != (byte)'%')
        break;

      while (pos < this._end && data[pos] != 10 && data[pos] != 13)
        ++pos;
    }

    this.Position = pos;
  }

  /// <summary>Reads the next object, folding <c>n g R</c> into a reference.</summary>
  /// <remarks>Returns <see cref="EndOfData"/> when nothing is left.</remarks>
  public object? ReadObject() => this._ReadObject(0, true);

  /// <summary>Reads the next object without treating <c>n g R</c> as a reference, as content streams need.</summary>
  public object? ReadContentObject() => this._ReadObject(0, false);

  /// <summary>Stands in for "no more objects", which null cannot since null is a PDF object.</summary>
  public static readonly object EndOfData = new();

  /// <summary>Stands in for a closing bracket or brace met where an object was expected.</summary>
  private static readonly object _Closer = new();

  private object? _ReadObject(int depth, bool references) {
    this.SkipWhitespace();
    if (this.Position >= this._end)
      return EndOfData;

    var data = this._data;
    var b = data[this.Position];
    switch (b) {
      case (byte)'/':
        return this._ReadName();
      case (byte)'(':
        return this._ReadLiteralString();
      case (byte)'[':
        ++this.Position;
        return this._ReadArray(depth + 1, references);
      case (byte)']' or (byte)'}' or (byte)')':
        ++this.Position;
        return _Closer;
      case (byte)'<':
        if (this.Position + 1 < this._end && data[this.Position + 1] == (byte)'<') {
          this.Position += 2;
          return this._ReadDictionary(depth + 1, references);
        }

        return this._ReadHexString();
      case (byte)'>':
        // A lone '>' or a '>>' with nothing open; step over it.
        ++this.Position;
        if (this.Position < this._end && data[this.Position] == (byte)'>')
          ++this.Position;
        return _Closer;
      case (byte)'{':
        ++this.Position;
        return new PdfOperator("{");
    }

    if (b is >= (byte)'0' and <= (byte)'9' or (byte)'+' or (byte)'-' or (byte)'.') {
      var number = this._ReadNumber();
      if (references && number is int objectNumber && objectNumber >= 0)
        return this._TryReference(objectNumber) ?? number;

      return number;
    }

    var word = this._ReadWord();
    return word switch {
      "true" => true,
      "false" => false,
      "null" => null,
      "" => this._SkipStray(),
      _ => new PdfOperator(word),
    };
  }

  private object? _SkipStray() {
    ++this.Position;
    return this._ReadObject(0, false);
  }

  private object? _TryReference(int objectNumber) {
    var saved = this.Position;
    this.SkipWhitespace();
    var data = this._data;
    var pos = this.Position;
    var genStart = pos;
    while (pos < this._end && data[pos] is >= (byte)'0' and <= (byte)'9')
      ++pos;

    if (pos == genStart || pos - genStart > 6 || pos >= this._end || !(IsWhitespace(data[pos]) || IsDelimiter(data[pos]))) {
      this.Position = saved;
      return null;
    }

    var generation = 0;
    for (var i = genStart; i < pos; ++i)
      generation = generation * 10 + (data[i] - '0');

    this.Position = pos;
    this.SkipWhitespace();
    pos = this.Position;
    if (pos < this._end && data[pos] == (byte)'R' && (pos + 1 >= this._end || IsWhitespace(data[pos + 1]) || IsDelimiter(data[pos + 1]))) {
      this.Position = pos + 1;
      return new PdfRef(objectNumber, generation);
    }

    this.Position = saved;
    return null;
  }

  private List<object?> _ReadArray(int depth, bool references) {
    var list = new List<object?>();
    if (depth > _MaxDepth)
      throw new System.IO.InvalidDataException("PDF arrays nest deeper than any real document does.");

    while (true) {
      var item = this._ReadObject(depth, references);
      if (item == EndOfData || item == _Closer)
        return list;

      list.Add(item);
    }
  }

  private PdfDict _ReadDictionary(int depth, bool references) {
    var dict = new PdfDict();
    if (depth > _MaxDepth)
      throw new System.IO.InvalidDataException("PDF dictionaries nest deeper than any real document does.");

    while (true) {
      this.SkipWhitespace();
      if (this.Position >= this._end)
        return dict;

      var b = this._data[this.Position];
      if (b == (byte)'>') {
        ++this.Position;
        if (this.Position < this._end && this._data[this.Position] == (byte)'>')
          ++this.Position;
        return dict;
      }

      var key = this._ReadObject(depth, references);
      if (key == EndOfData)
        return dict;
      if (key is not string name) {
        // A key that is not a name: a keyword such as "endobj" means the dictionary was never
        // closed, which some writers manage; anything else is skipped.
        if (key is PdfOperator { Name: "endobj" or "stream" or "endstream" or "obj" }) {
          this.Position -= ((PdfOperator)key).Name.Length;
          return dict;
        }

        continue;
      }

      var value = this._ReadObject(depth, references);
      if (value == EndOfData)
        return dict;
      if (value == _Closer) {
        // "/Key >>": the value was left out.
        return dict;
      }

      if (value is PdfOperator { Name: "endobj" or "stream" or "endstream" } stray) {
        this.Position -= stray.Name.Length;
        return dict;
      }

      dict[name] = value;
    }
  }

  private string _ReadName() {
    var data = this._data;
    var pos = this.Position + 1;
    var start = pos;
    var plain = true;
    while (pos < this._end) {
      var b = data[pos];
      if (IsWhitespace(b) || IsDelimiter(b))
        break;
      if (b == (byte)'#')
        plain = false;
      ++pos;
    }

    this.Position = pos;
    if (plain)
      return Encoding.Latin1.GetString(data, start, pos - start);

    var sb = new StringBuilder(pos - start);
    for (var i = start; i < pos; ++i) {
      var b = data[i];
      if (b == (byte)'#' && i + 2 < pos) {
        var hi = _HexValue(data[i + 1]);
        var lo = _HexValue(data[i + 2]);
        if (hi >= 0 && lo >= 0) {
          sb.Append((char)(hi * 16 + lo));
          i += 2;
          continue;
        }
      }

      sb.Append((char)b);
    }

    return sb.ToString();
  }

  private byte[] _ReadLiteralString() {
    var data = this._data;
    var pos = this.Position + 1;
    var output = new List<byte>();
    var depth = 1;
    while (pos < this._end) {
      var b = data[pos++];
      if (b == (byte)'\\') {
        if (pos >= this._end)
          break;

        var e = data[pos++];
        switch (e) {
          case (byte)'n': output.Add(10); break;
          case (byte)'r': output.Add(13); break;
          case (byte)'t': output.Add(9); break;
          case (byte)'b': output.Add(8); break;
          case (byte)'f': output.Add(12); break;
          case 13:
            // A backslash at the end of a line continues the string on the next.
            if (pos < this._end && data[pos] == 10)
              ++pos;
            break;
          case 10:
            break;
          case >= (byte)'0' and <= (byte)'7': {
            var value = e - '0';
            for (var n = 0; n < 2 && pos < this._end && data[pos] is >= (byte)'0' and <= (byte)'7'; ++n)
              value = value * 8 + (data[pos++] - '0');
            output.Add((byte)value);
            break;
          }
          default:
            output.Add(e);
            break;
        }

        continue;
      }

      if (b == (byte)'(')
        ++depth;
      else if (b == (byte)')' && --depth == 0)
        break;

      output.Add(b);
    }

    this.Position = pos;
    return output.ToArray();
  }

  private byte[] _ReadHexString() {
    var data = this._data;
    var pos = this.Position + 1;
    var output = new List<byte>();
    var high = -1;
    while (pos < this._end) {
      var b = data[pos++];
      if (b == (byte)'>')
        break;

      var value = _HexValue(b);
      if (value < 0)
        continue;

      if (high < 0)
        high = value;
      else {
        output.Add((byte)(high * 16 + value));
        high = -1;
      }
    }

    if (high >= 0)
      output.Add((byte)(high * 16));

    this.Position = pos;
    return output.ToArray();
  }

  private object _ReadNumber() {
    var data = this._data;
    var pos = this.Position;
    var negative = false;
    while (pos < this._end && data[pos] is (byte)'+' or (byte)'-') {
      negative ^= data[pos] == (byte)'-';
      ++pos;
    }

    long whole = 0;
    var digits = 0;
    while (pos < this._end && data[pos] is >= (byte)'0' and <= (byte)'9') {
      if (whole < long.MaxValue / 10)
        whole = whole * 10 + (data[pos] - '0');
      ++pos;
      ++digits;
    }

    var isReal = false;
    double fraction = 0;
    if (pos < this._end && data[pos] == (byte)'.') {
      isReal = true;
      ++pos;
      var scale = 0.1;
      while (pos < this._end && data[pos] is >= (byte)'0' and <= (byte)'9') {
        fraction += (data[pos] - '0') * scale;
        scale *= 0.1;
        ++pos;
      }

      // A second point or a stray minus inside a number is junk some writers emit; skip it.
      while (pos < this._end && data[pos] is (byte)'.' or (byte)'-' or >= (byte)'0' and <= (byte)'9')
        ++pos;
    } else if (pos < this._end && data[pos] is (byte)'e' or (byte)'E' && digits > 0) {
      // Not PDF syntax, but some generators write exponents; only accept a well-formed one.
      var probe = pos + 1;
      if (probe < this._end && data[probe] is (byte)'+' or (byte)'-')
        ++probe;
      if (probe < this._end && data[probe] is >= (byte)'0' and <= (byte)'9') {
        var mantissaStart = pos - digits;
        pos = probe;
        while (pos < this._end && data[pos] is >= (byte)'0' and <= (byte)'9')
          ++pos;
        this.Position = pos;
        var text = Encoding.ASCII.GetString(data, mantissaStart, pos - mantissaStart);
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var exp) ? (negative ? -exp : exp) : 0.0;
      }
    }

    this.Position = pos;
    if (isReal) {
      var value = whole + fraction;
      return negative ? -value : value;
    }

    if (digits == 0)
      return 0;

    var signed = negative ? -whole : whole;
    return signed is >= int.MinValue and <= int.MaxValue ? (object)(int)signed : (double)signed;
  }

  private string _ReadWord() {
    var data = this._data;
    var pos = this.Position;
    var start = pos;
    while (pos < this._end) {
      var b = data[pos];
      if (IsWhitespace(b) || IsDelimiter(b))
        break;
      ++pos;
    }

    this.Position = pos;
    return pos == start ? string.Empty : Encoding.Latin1.GetString(data, start, pos - start);
  }

  /// <summary>Whether the bytes at a position spell a keyword followed by a delimiter.</summary>
  public static bool MatchesKeyword(byte[] data, int pos, ReadOnlySpan<byte> keyword, int end = -1) {
    if (end < 0)
      end = data.Length;
    if (pos < 0 || pos + keyword.Length > end)
      return false;

    for (var i = 0; i < keyword.Length; ++i)
      if (data[pos + i] != keyword[i])
        return false;

    var after = pos + keyword.Length;
    return after >= end || IsWhitespace(data[after]) || IsDelimiter(data[after]);
  }

  private static int _HexValue(byte b) => b switch {
    >= (byte)'0' and <= (byte)'9' => b - '0',
    >= (byte)'a' and <= (byte)'f' => b - 'a' + 10,
    >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
    _ => -1,
  };
}
