using System;
using System.Collections.Generic;
using FileFormat.Pdf.Document;

namespace FileFormat.Pdf.Rendering;

/// <summary>
/// A CMap mapping character codes to CIDs (PDF 32000-1 §9.7.5): codespace ranges to split a string
/// into codes, and CID ranges and single mappings.
/// </summary>
/// <remarks>
/// Embedded CMaps are parsed from their PostScript text (begincodespacerange, begincidrange,
/// begincidchar, beginnotdefrange, usecmap, /WMode). Identity-H and Identity-V are built in. The
/// other predefined CMaps — the CJK encodings of Adobe's cmap-resources — are data this package does
/// not carry, and are refused by name.
/// </remarks>
internal sealed class PdfCMap {

  private readonly List<(int Bytes, uint Low, uint High)> _codespaces = [];
  private readonly List<(int Bytes, uint Low, uint High, int Cid)> _ranges = [];
  private readonly Dictionary<(int, uint), int> _singles = [];
  private bool _identity;

  /// <summary>Whether text in this CMap is written vertically.</summary>
  public bool Vertical { get; private set; }

  public static PdfCMap Identity(bool vertical) {
    var map = new PdfCMap { _identity = true, Vertical = vertical };
    map._codespaces.Add((2, 0, 0xFFFF));
    return map;
  }

  /// <summary>Reads a CMap from a name or a stream.</summary>
  public static PdfCMap Parse(PdfDocument document, object? value, int depth = 0) {
    value = document.Resolve(value);
    if (value is string name)
      return name switch {
        "Identity-H" => Identity(false),
        "Identity-V" => Identity(true),
        _ => throw new NotSupportedException($"The predefined CMap /{name} is not available; only Identity-H and Identity-V are built in."),
      };

    if (value is not PdfStreamObject stream)
      throw new NotSupportedException("A font's /Encoding CMap is neither a name nor a stream.");

    var map = new PdfCMap();
    if (document.Get(stream.Dictionary, "UseCMap") is { } parent && depth < 8)
      map._Inherit(Parse(document, parent, depth + 1));
    if (document.Get(stream.Dictionary, "WMode") is { } wmode)
      map.Vertical = PdfValues.Integer(wmode) == 1;

    map._Read(document, document.Decode(stream), depth);
    if (map._codespaces.Count == 0)
      map._codespaces.Add((2, 0, 0xFFFF));
    return map;
  }

  private void _Inherit(PdfCMap parent) {
    this._identity |= parent._identity && this._ranges.Count == 0 && this._singles.Count == 0;
    this.Vertical = parent.Vertical;
    this._codespaces.AddRange(parent._codespaces);
    this._ranges.AddRange(parent._ranges);
    foreach (var (key, value) in parent._singles)
      this._singles[key] = value;
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
              this._ranges.Add((low.Length, _Value(low), _Value(high), PdfValues.Integer(values[2])));
          });
          break;
        case "begincidchar":
          this._Pairs(lexer, "endcidchar", 2, values => {
            if (values[0] is byte[] code && PdfValues.IsNumber(values[1]))
              this._singles[(code.Length, _Value(code))] = PdfValues.Integer(values[1]);
          });
          break;
        case "beginnotdefrange":
          this._Pairs(lexer, "endnotdefrange", 3, _ => { });
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
        var shortest = 4;
        foreach (var (bytes, _, _) in this._codespaces)
          shortest = Math.Min(shortest, bytes);
        length = Math.Max(1, Math.Min(shortest, text.Length - pos));
        code = 0;
        for (var i = 0; i < length; ++i)
          code = (code << 8) | text[pos + i];
      }

      yield return (code, length);
      pos += length;
    }
  }

  /// <summary>The CID for a code of a given length, 0 when the CMap does not map it.</summary>
  public int Cid(uint code, int length) {
    if (this._singles.TryGetValue((length, code), out var single))
      return single;

    for (var i = this._ranges.Count - 1; i >= 0; --i) {
      var (bytes, low, high, cid) = this._ranges[i];
      if (bytes == length && code >= low && code <= high)
        return cid + (int)(code - low);
    }

    return this._identity ? (int)code : 0;
  }
}
