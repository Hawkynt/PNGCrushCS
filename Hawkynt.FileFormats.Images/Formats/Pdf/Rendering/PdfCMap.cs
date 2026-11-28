using System;
using System.Collections.Generic;
using FileFormat.Pdf.Document;
using FileFormat.Pdf.Fonts.Data;

namespace FileFormat.Pdf.Rendering;

/// <summary>
/// A CMap mapping character codes to CIDs (PDF 32000-1 §9.7.5): codespace ranges to split a string
/// into codes, CID ranges and single mappings, and notdef ranges for codes nothing else maps.
/// </summary>
/// <remarks>
/// Embedded CMaps are parsed from their PostScript text (begincodespacerange, begincidrange,
/// begincidchar, beginnotdefrange, usecmap, /WMode). Identity-H and Identity-V are built in; every
/// other predefined CMap — the CJK encodings of table 118 and the rest of Adobe's cmap-resources —
/// comes from the bundled set in <see cref="PdfPredefinedCMaps"/>.
/// <para/>
/// Mappings are kept in the order they are defined, a CMap's own after those of the one it uses, and
/// a later mapping of a code wins over an earlier one. Lookups go through tables built on first use.
/// </remarks>
internal sealed class PdfCMap {

  private readonly List<(int Bytes, uint Low, uint High)> _codespaces = [];
  private readonly List<(int Bytes, uint Low, uint High, int Cid)> _mappings = [];
  private readonly List<(int Bytes, uint Low, uint High, int Cid)> _notdefs = [];
  private bool _identity;
  private int[]?[]? _small;
  private Dictionary<(int, uint), int>? _large;
  private int _shortest = 4;

  /// <summary>Whether text in this CMap is written vertically.</summary>
  public bool Vertical { get; private set; }

  /// <summary>The CMap's name when it is a predefined one, for choosing a substitute font's character collection.</summary>
  public string? PredefinedName { get; private set; }

  public static PdfCMap Identity(bool vertical) {
    var map = new PdfCMap { _identity = true, Vertical = vertical, PredefinedName = vertical ? "Identity-V" : "Identity-H" };
    map._codespaces.Add((2, 0, 0xFFFF));
    return map;
  }

  /// <summary>Reads a CMap from a name or a stream.</summary>
  public static PdfCMap Parse(PdfDocument document, object? value, int depth = 0) {
    value = document.Resolve(value);
    if (value is string name)
      return Predefined(name, depth) ?? throw new NotSupportedException($"The CMap /{name} is neither embedded nor one of the predefined CMaps.");

    if (value is not PdfStreamObject stream)
      throw new NotSupportedException("A font's /Encoding CMap is neither a name nor a stream.");

    var map = new PdfCMap();
    if (document.Get(stream.Dictionary, "UseCMap") is { } parent && depth < 8)
      map._Inherit(Parse(document, parent, depth + 1));
    if (document.Get(stream.Dictionary, "WMode") is { } wmode)
      map.Vertical = PdfValues.Integer(wmode) == 1;

    map._Read(document, document.Decode(stream), depth);
    map._Finish();
    return map;
  }

  private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, PdfCMap?> _PredefinedCache = new(StringComparer.Ordinal);

  /// <summary>A predefined CMap by name, or null when there is no such CMap; each is built once and shared.</summary>
  public static PdfCMap? Predefined(string name, int depth = 0)
    => depth == 0 ? _PredefinedCache.GetOrAdd(name, static n => _Predefined(n, 0)) : _Predefined(name, depth);

  private static PdfCMap? _Predefined(string name, int depth) {
    switch (name) {
      case "Identity-H":
        return Identity(false);
      case "Identity-V":
        return Identity(true);
    }

    if (depth > 8 || PdfPredefinedCMaps.Find(name) is not { } record)
      return null;

    var map = new PdfCMap { PredefinedName = name, Vertical = record.Vertical };
    if (record.UseCMap is { Length: > 0 } use && _Predefined(use, depth + 1) is { } parent) {
      map._Inherit(parent);
      map.Vertical = record.Vertical;
    }

    map._codespaces.AddRange(record.Codespaces);
    map._mappings.AddRange(record.Mappings);
    map._notdefs.AddRange(record.Notdefs);
    map._Finish();
    return map;
  }

  private void _Finish() {
    if (this._codespaces.Count == 0)
      this._codespaces.Add((2, 0, 0xFFFF));
    this._shortest = 4;
    foreach (var (bytes, _, _) in this._codespaces)
      this._shortest = Math.Min(this._shortest, bytes);
  }

  private void _Inherit(PdfCMap parent) {
    this._identity |= parent._identity && this._mappings.Count == 0;
    this.Vertical = parent.Vertical;
    this._codespaces.AddRange(parent._codespaces);
    this._mappings.AddRange(parent._mappings);
    this._notdefs.AddRange(parent._notdefs);
  }

  private void _Read(PdfDocument document, byte[] data, int depth) {
    var lexer = new PdfLexer(data);
    var operands = new List<object?>();
    while (true) {
      var token = lexer.ReadContentObject();
      if (token == PdfLexer.EndOfData)
        break;
      if (token is not PdfOperator op) {
        operands.Add(token);
        continue;
      }

      switch (op.Name) {
        case "begincodespacerange":
          this._Pairs(lexer, "endcodespacerange", 2, values => {
            if (values[0] is byte[] low && values[1] is byte[] high)
              this._codespaces.Add((low.Length, _Value(low), _Value(high)));
          });
          break;
        case "begincidrange":
          this._Pairs(lexer, "endcidrange", 3, values => {
            if (values[0] is byte[] low && values[1] is byte[] high && PdfValues.IsNumber(values[2]))
              this._mappings.Add((low.Length, _Value(low), _Value(high), PdfValues.Integer(values[2])));
          });
          break;
        case "begincidchar":
          this._Pairs(lexer, "endcidchar", 2, values => {
            if (values[0] is byte[] code && PdfValues.IsNumber(values[1]))
              this._mappings.Add((code.Length, _Value(code), _Value(code), PdfValues.Integer(values[1])));
          });
          break;
        case "beginnotdefrange":
          this._Pairs(lexer, "endnotdefrange", 3, values => {
            if (values[0] is byte[] low && values[1] is byte[] high && PdfValues.IsNumber(values[2]))
              this._notdefs.Add((low.Length, _Value(low), _Value(high), PdfValues.Integer(values[2])));
          });
          break;
        case "usecmap":
          if (operands.Count > 0 && operands[^1] is string parent && depth < 8)
            this._Inherit(Parse(document, parent, depth + 1));
          break;
        case "def":
          if (operands.Count >= 2 && operands[^2] is "WMode")
            this.Vertical = PdfValues.Integer(operands[^1]) == 1;
          break;
      }

      operands.Clear();
    }
  }

  private void _Pairs(PdfLexer lexer, string end, int width, Action<object?[]> take) {
    var values = new object?[width];
    var count = 0;
    while (true) {
      var token = lexer.ReadContentObject();
      if (token == PdfLexer.EndOfData || token is PdfOperator { Name: var name } && name == end)
        return;

      values[count++] = token;
      if (count == width) {
        take(values);
        count = 0;
      }
    }
  }

  private static uint _Value(byte[] bytes) {
    uint value = 0;
    foreach (var b in bytes)
      value = (value << 8) | b;
    return value;
  }

  /// <summary>Splits a string into codes by the codespace ranges, longest match from one byte up.</summary>
  public IEnumerable<(uint Code, int Length)> Codes(byte[] text) {
    var pos = 0;
    while (pos < text.Length) {
      var length = 0;
      uint code = 0;
      for (var n = 1; n <= 4 && pos + n <= text.Length && length == 0; ++n) {
        code = (code << 8) | text[pos + n - 1];
        foreach (var (bytes, low, high) in this._codespaces)
          if (bytes == n && code >= low && code <= high) {
            length = n;
            break;
          }
      }

      if (length == 0) {
        // No codespace matches: take as many bytes as the shortest range, as the spec suggests.
        length = Math.Max(1, Math.Min(this._shortest, text.Length - pos));
        code = 0;
        for (var i = 0; i < length; ++i)
          code = (code << 8) | text[pos + i];
      }

      yield return (code, length);
      pos += length;
    }
  }

  /// <summary>The CID for a code of a given length: its mapping, else its notdef mapping, else 0.</summary>
  public int Cid(uint code, int length) {
    this._Build();
    if (length <= 2) {
      var table = this._small![length];
      if (table != null && code < (uint)table.Length && table[code] >= 0)
        return table[code];
    } else if (this._large!.TryGetValue((length, code), out var cid))
      return cid;

    return this._identity ? (int)code : 0;
  }

  /// <summary>Every code of a length this CMap maps, with its CID (notdef ranges left out).</summary>
  public IEnumerable<(uint Code, int Length, int Cid)> Mappings() {
    foreach (var (bytes, low, high, cid) in this._mappings)
      for (var c = low; c <= high && c >= low; ++c)
        yield return (c, bytes, cid + (int)(c - low));
  }

  private void _Build() {
    if (this._small != null)
      return;

    var small = new int[]?[3];
    var large = new Dictionary<(int, uint), int>();

    // A notdef range maps every code in it to the one CID; a CID range counts up from its CID.
    void Apply(List<(int Bytes, uint Low, uint High, int Cid)> list, int step) {
      foreach (var (bytes, low, high, cid) in list) {
        if (bytes is < 1 or > 4 || high < low)
          continue;
        if (bytes <= 2) {
          var table = small[bytes] ??= _NewTable(bytes == 1 ? 256 : 65536);
          for (uint c = low; c <= high && c < table.Length; ++c)
            table[c] = cid + step * (int)(c - low);
        } else {
          // Large ranges in 3- and 4-byte CMaps are rare and short; guard against absurd ones.
          var end = Math.Min(high, low + 0x10000);
          for (var c = low; c <= end && c >= low; ++c)
            large[(bytes, c)] = cid + step * (int)(c - low);
        }
      }
    }

    // Notdef mappings first, so a real mapping of the same code replaces them.
    Apply(this._notdefs, 0);
    Apply(this._mappings, 1);
    this._large = large;
    this._small = small;
  }

  private static int[] _NewTable(int size) {
    var table = new int[size];
    Array.Fill(table, -1);
    return table;
  }
}
