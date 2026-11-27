using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FileFormat.Pdf.Fonts;

/// <summary>A Type 1 font program, as a PDF embeds it in a FontFile stream.</summary>
/// <remarks>
/// The program is PostScript, but nothing in it has to be run to draw with it: the cleartext part
/// states the matrix and encoding, the eexec-encrypted part holds the Subrs and CharStrings in a
/// fixed pattern. This scans for those the way pdf.js's <c>Type1Parser</c> does — token by token,
/// taking the binary that follows each <c>RD</c> by its stated length — rather than interpreting the
/// PostScript, which is what every viewer does and what lets a slightly unusual font still load.
/// </remarks>
internal sealed class Type1FontProgram : IPdfFontProgram {

  private const int _EexecKey = 55665;
  private const int _CharStringKey = 4330;

  private readonly List<string> _names;
  private readonly List<byte[]> _charStrings;
  private readonly List<byte[]?> _subrs;
  private readonly Dictionary<string, int> _gidByName = new(StringComparer.Ordinal);
  private readonly double[] _matrix;
  private readonly Dictionary<int, string> _encoding;
  private readonly GlyphOutline?[] _outlines;
  private readonly double[] _widths;
  private readonly bool[] _parsed;

  private Type1FontProgram(List<string> names, List<byte[]> charStrings, List<byte[]?> subrs, double[] matrix, Dictionary<int, string> encoding) {
    this._names = names;
    this._charStrings = charStrings;
    this._subrs = subrs;
    this._matrix = matrix;
    this._encoding = encoding;
    for (var gid = 0; gid < names.Count; ++gid)
      this._gidByName.TryAdd(names[gid], gid);

    this._outlines = new GlyphOutline?[names.Count];
    this._widths = new double[names.Count];
    this._parsed = new bool[names.Count];
  }

  /// <inheritdoc/>
  public int GlyphCount => this._names.Count;

  /// <inheritdoc/>
  public IReadOnlyDictionary<int, string>? BuiltInEncoding => this._encoding;

  /// <summary>The font matrix from the cleartext part.</summary>
  public IReadOnlyList<double> FontMatrix => this._matrix;

  /// <summary>The name of a glyph, or null for an id out of range.</summary>
  public string? GlyphName(int glyphId) => (uint)glyphId < (uint)this._names.Count ? this._names[glyphId] : null;

  /// <inheritdoc/>
  public int GlyphIdOfName(string name) => this._gidByName.TryGetValue(name, out var gid) ? gid : -1;

  /// <inheritdoc/>
  public GlyphOutline? Outline(int glyphId) {
    if ((uint)glyphId >= (uint)this._names.Count)
      return null;

    this._Ensure(glyphId);
    return this._outlines[glyphId];
  }

  /// <inheritdoc/>
  public double AdvanceWidth(int glyphId) {
    if ((uint)glyphId >= (uint)this._names.Count)
      return double.NaN;

    this._Ensure(glyphId);
    return this._widths[glyphId];
  }

  /// <summary>Reads a Type 1 font program.</summary>
  /// <param name="data">The FontFile stream's decoded bytes, or a PFA/PFB file.</param>
  /// <param name="length1">The stream's Length1: how long the cleartext part is; 0 when unknown.</param>
  /// <param name="length2">The stream's Length2: how long the encrypted part is; 0 when unknown.</param>
  /// <exception cref="InvalidDataException">The data holds no recognisable Type 1 program.</exception>
  public static Type1FontProgram Parse(byte[] data, int length1, int length2) {
    ArgumentNullException.ThrowIfNull(data);

    var (header, encrypted) = _Split(data, length1, length2);
    var matrix = new double[] { 0.001, 0, 0, 0.001, 0, 0 };
    var encoding = new Dictionary<int, string>();
    _ReadHeader(header, matrix, encoding);

    var plain = _IsHex(encrypted) ? _DecryptHex(encrypted, _EexecKey, 4) : _Decrypt(encrypted, _EexecKey, 4);
    var names = new List<string>();
    var charStrings = new List<byte[]>();
    var subrs = new List<byte[]?>();
    _ReadPrivate(plain, names, charStrings, subrs);
    if (names.Count == 0)
      throw new InvalidDataException("Type 1 font has no CharStrings.");

    return new(names, charStrings, subrs, matrix, encoding);
  }

  private void _Ensure(int glyphId) {
    if (this._parsed[glyphId])
      return;

    this._parsed[glyphId] = true;
    var m = this._matrix;
    try {
      var outline = new GlyphOutline();
      var width = this._Interpret(glyphId, outline, 0, 0, 0);
      this._outlines[glyphId] = outline.Transformed(m[0], m[1], m[2], m[3], m[4], m[5]);
      this._widths[glyphId] = width * m[0];
    } catch (InvalidDataException) {
      // FreeType drops a glyph it cannot run, and pdf.js mimics it; so does this.
      this._outlines[glyphId] = new GlyphOutline();
      this._widths[glyphId] = double.NaN;
    }
  }

  private double _Interpret(int glyphId, GlyphOutline outline, double offsetX, double offsetY, int depth) {
    var interpreter = new Type1CharStringInterpreter(this._subrs, outline, offsetX, offsetY);
    interpreter.Run(this._charStrings[glyphId]);

    if (interpreter.Seac is { } seac && depth == 0) {
      var baseName = Type1StandardEncoding.NameOf(seac.BaseCode);
      var accentName = Type1StandardEncoding.NameOf(seac.AccentCode);
      var baseGid = baseName == null ? -1 : this.GlyphIdOfName(baseName);
      var accentGid = accentName == null ? -1 : this.GlyphIdOfName(accentName);
      if (baseGid >= 0)
        this._Interpret(baseGid, outline, offsetX, offsetY, depth + 1);
      if (accentGid >= 0)
        this._Interpret(accentGid, outline, offsetX + seac.Adx, offsetY + seac.Ady, depth + 1);
    }

    outline.Close();
    return interpreter.Width;
  }

  // ---------------------------------------------------------------------------------------------
  // Container
  // ---------------------------------------------------------------------------------------------

  private static (byte[] Header, byte[] Encrypted) _Split(byte[] data, int length1, int length2) {
    // PFB: segments of 0x80, type, little-endian length; type 1 is text, 2 is binary, 3 the end.
    if (data.Length > 6 && data[0] == 0x80 && data[1] is 1 or 2) {
      var text = new List<byte>();
      var binary = new List<byte>();
      var pos = 0;
      while (pos + 6 <= data.Length && data[pos] == 0x80 && data[pos + 1] is 1 or 2) {
        var type = data[pos + 1];
        var length = data[pos + 2] | (data[pos + 3] << 8) | (data[pos + 4] << 16) | (data[pos + 5] << 24);
        pos += 6;
        length = Math.Clamp(length, 0, data.Length - pos);
        // After the first binary segment, a text segment is the trailing zeros; skip it.
        if (type == 1 && binary.Count == 0)
          text.AddRange(data.AsSpan(pos, length).ToArray());
        else if (type == 2)
          binary.AddRange(data.AsSpan(pos, length).ToArray());
        pos += length;
      }

      if (binary.Count > 0)
        return (text.ToArray(), binary.ToArray());
    }

    var eexec = _Find(data, "eexec"u8, 0);
    if (eexec < 0)
      throw new InvalidDataException("Type 1 font has no eexec section.");

    var start = eexec + 5;
    var afterWhitespace = start;
    while (afterWhitespace < data.Length && data[afterWhitespace] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
      ++afterWhitespace;

    // Length1 is trusted when it lands in the whitespace after eexec: the binary may itself begin
    // with a byte that looks like whitespace, which skipping alone would eat.
    var bodyStart = length1 >= start && length1 <= afterWhitespace ? length1 : afterWhitespace;
    var bodyLength = data.Length - bodyStart;
    if (length2 > 0 && length2 < bodyLength && !_IsHex(data.AsSpan(bodyStart)))
      bodyLength = length2;

    return (data[..eexec], data.AsSpan(bodyStart, bodyLength).ToArray());
  }

  private static void _ReadHeader(byte[] header, double[] matrix, Dictionary<int, string> encoding) {
    var lexer = new _Lexer(header);
    while (lexer.Next() is { } token) {
      if (token != "/")
        continue;

      switch (lexer.Next()) {
        case "FontMatrix": {
          var values = lexer.ReadNumberArray();
          if (values.Count >= 6)
            for (var i = 0; i < 6; ++i)
              matrix[i] = values[i];
          break;
        }

        case "Encoding": {
          var argument = lexer.Next();
          if (argument == null)
            return;

          if (!int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size)) {
            if (argument == "StandardEncoding")
              for (var code = 0; code < 256; ++code)
                if (Type1StandardEncoding.NameOf(code) is { } name)
                  encoding[code] = name;
            break;
          }

          // "n array 0 1 n-1 {1 index exch /.notdef put} for dup c /name put ... readonly def"
          lexer.Next(); // array
          for (var entry = 0; entry < Math.Max(size, 256); ++entry) {
            var word = lexer.Next();
            while (word != null && word != "dup" && word != "def")
              word = lexer.Next();
            if (word is null or "def")
              break;

            var codeText = lexer.Next();
            if (lexer.Next() != "/")
              continue;
            var glyph = lexer.Next();
            lexer.Next(); // put
            if (glyph != null && int.TryParse(codeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code) && (uint)code < 256)
              encoding[code] = glyph;
          }
          break;
        }
      }
    }
  }

  private static void _ReadPrivate(byte[] plain, List<string> names, List<byte[]> charStrings, List<byte[]?> subrs) {
    var lexer = new _Lexer(plain);
    var lenIV = 4;
    var subrsRead = false;
    var charStringsRead = false;
    byte[]? notdef = null;

    while (lexer.Next() is { } token) {
      if (token != "/")
        continue;

      switch (lexer.Next()) {
        case "lenIV":
          lenIV = (int)lexer.ReadNumber();
          break;

        case "Subrs" when !subrsRead:
          subrsRead = true;
          lexer.Next(); // count
          lexer.Next(); // array
          while (lexer.Next() == "dup") {
            var index = (int)lexer.ReadNumber();
            var length = (int)lexer.ReadNumber();
            lexer.Next(); // RD or -|
            var bytes = lexer.ReadBinary(length);
            var word = lexer.Next(); // NP, |, or noaccess put
            if (word == "noaccess")
              lexer.Next();
            if (index is >= 0 and < 65536) {
              while (subrs.Count <= index)
                subrs.Add(null);
              subrs[index] = _CharString(bytes, lenIV);
            }
          }
          break;

        case "CharStrings" when !charStringsRead:
          charStringsRead = true;
          lexer.Next(); // count
          lexer.Next(); // dict
          lexer.Next(); // dup
          lexer.Next(); // begin
          while (lexer.Next() is { } word && word != "end") {
            if (word != "/")
              continue;

            var glyph = lexer.Next();
            var length = (int)lexer.ReadNumber();
            lexer.Next(); // RD or -|
            var bytes = _CharString(lexer.ReadBinary(length), lenIV);
            var after = lexer.Next(); // ND or |-
            if (after == "noaccess")
              lexer.Next();
            else if (after == "/")
              lexer.PutBack();
            if (glyph == null)
              continue;

            // .notdef goes to glyph zero, which is what a glyph id of zero means everywhere else.
            if (glyph == ".notdef" && notdef == null) {
              notdef = bytes;
              continue;
            }

            names.Add(glyph);
            charStrings.Add(bytes);
          }
          break;
      }
    }

    names.Insert(0, ".notdef");
    charStrings.Insert(0, notdef ?? [139, 139, 13, 14]); // 0 0 hsbw endchar
  }

  private static byte[] _CharString(byte[] encrypted, int lenIV) => lenIV < 0 ? encrypted : _Decrypt(encrypted, _CharStringKey, lenIV);

  // ---------------------------------------------------------------------------------------------
  // eexec / charstring encryption (Type 1 spec, chapter 7)
  // ---------------------------------------------------------------------------------------------

  private static byte[] _Decrypt(ReadOnlySpan<byte> data, int key, int discard) {
    if (discard >= data.Length)
      return [];

    const int c1 = 52845, c2 = 22719;
    var r = key;
    var result = new byte[data.Length - Math.Max(discard, 0)];
    for (var i = 0; i < data.Length; ++i) {
      var value = data[i];
      var plain = (byte)(value ^ (r >> 8));
      r = ((value + r) * c1 + c2) & 0xFFFF;
      if (i >= discard)
        result[i - discard] = plain;
    }

    return result;
  }

  private static byte[] _DecryptHex(ReadOnlySpan<byte> data, int key, int discard) {
    var bytes = new List<byte>(data.Length / 2);
    var high = -1;
    foreach (var b in data) {
      var digit = _HexDigit(b);
      if (digit < 0)
        continue;
      if (high < 0) {
        high = digit;
        continue;
      }

      bytes.Add((byte)((high << 4) | digit));
      high = -1;
    }

    return _Decrypt(bytes.ToArray(), key, discard);
  }

  // The test pdf.js uses: an eexec section written in hex starts with eight hex digits.
  private static bool _IsHex(ReadOnlySpan<byte> data) {
    if (data.Length < 8)
      return false;

    if (_HexDigit(data[0]) < 0 && data[0] is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'))
      return false;

    for (var i = 1; i < 8; ++i)
      if (_HexDigit(data[i]) < 0)
        return false;

    return true;
  }

  private static int _HexDigit(byte b) => b switch {
    >= (byte)'0' and <= (byte)'9' => b - '0',
    >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
    >= (byte)'a' and <= (byte)'f' => b - 'a' + 10,
    _ => -1,
  };

  private static int _Find(byte[] data, ReadOnlySpan<byte> needle, int from) {
    var index = data.AsSpan(from).IndexOf(needle);
    return index < 0 ? -1 : index + from;
  }

  /// <summary>The PostScript tokenizer of pdf.js's Type1Parser: whitespace and comments skipped, delimiters single tokens.</summary>
  private sealed class _Lexer(byte[] data) {

    private int _pos;
    private int _previous;

    public string? Next() {
      this._previous = this._pos;
      while (this._pos < data.Length) {
        var b = data[this._pos];
        if (b == (byte)'%') {
          while (this._pos < data.Length && data[this._pos] is not ((byte)'\n' or (byte)'\r'))
            ++this._pos;
          continue;
        }

        if (!_IsWhitespace(b))
          break;
        ++this._pos;
      }

      if (this._pos >= data.Length)
        return null;

      var first = data[this._pos];
      if (_IsSpecial(first)) {
        ++this._pos;
        return ((char)first).ToString();
      }

      var start = this._pos;
      while (this._pos < data.Length && !_IsWhitespace(data[this._pos]) && !_IsSpecial(data[this._pos]))
        ++this._pos;

      return Encoding.Latin1.GetString(data, start, this._pos - start);
    }

    public void PutBack() => this._pos = this._previous;

    public double ReadNumber() => double.TryParse(this.Next(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;

    public List<double> ReadNumberArray() {
      var values = new List<double>();
      this.Next(); // [ or {
      while (this.Next() is { } token && token != "]" && token != "}")
        values.Add(double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0);

      return values;
    }

    // The binary starts one byte after the RD token, which is the separator PostScript requires.
    public byte[] ReadBinary(int length) {
      var start = this._pos + 1;
      if (length <= 0 || start >= data.Length) {
        this._pos = Math.Min(start, data.Length);
        return [];
      }

      length = Math.Min(length, data.Length - start);
      this._pos = start + length;
      return data.AsSpan(start, length).ToArray();
    }

    private static bool _IsWhitespace(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or (byte)'\f' or 0;

    private static bool _IsSpecial(byte b) => b is (byte)'/' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'(' or (byte)')';
  }
}
