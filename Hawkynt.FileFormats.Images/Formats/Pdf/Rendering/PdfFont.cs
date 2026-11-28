using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core.Vector;
using FileFormat.Pdf.Document;
using FileFormat.Pdf.Fonts;
using FileFormat.Pdf.Fonts.Data;

namespace FileFormat.Pdf.Rendering;

/// <summary>
/// A font as the text operators use it (PDF 32000-1 §9.5–§9.10): how a string splits into codes,
/// how far each code advances, and which outline it draws.
/// </summary>
/// <remarks>
/// Simple fonts map a byte through their encoding — a base encoding (named, the font program's own,
/// or Standard) with /Differences on top — to a glyph name, and the name to a glyph of the program.
/// TrueType programs are reached by the rules of §9.6.6.4: a Unicode (3,1) cmap through the Adobe
/// Glyph List for a non-symbolic font, the (3,0) symbol cmap at code, F000+code, F100+code and
/// F200+code for a symbolic one, the Macintosh (1,0) cmap otherwise, then the post table's names,
/// and finally the code as a glyph index, which is what a subset with no cmap at all means.
/// Composite fonts split strings by their CMap and map CIDs to glyphs through /CIDToGIDMap or the
/// CFF charset.
/// <para/>
/// A font that is not embedded is drawn with a bundled Liberation face standing in for Helvetica,
/// Times or Courier — the families those are metric-compatible with — chosen from the base font name
/// and the descriptor's flags; Symbol and ZapfDingbats with the bundled Noto-based symbol face. A
/// non-embedded CID font of Adobe-Japan1, -GB1, -CNS1 or -Korea1 is drawn with an installed font that
/// covers its collection (<see cref="PdfSystemFonts"/>, <see cref="PdfCidSubstitute"/>), at the PDF
/// font's own widths. Where nothing can stand in the text is not drawn and the omission is reported.
/// </remarks>
internal sealed class PdfFont {

  private readonly PdfDocument _document;
  private readonly Dictionary<int, GlyphOutline?> _outlines = [];

  /// <summary>The /BaseFont, for messages.</summary>
  public string Name { get; private set; } = "?";

  /// <summary>A Type 0 font, whose strings are split by a CMap.</summary>
  public bool IsComposite { get; private set; }

  /// <summary>A Type 3 font, whose glyphs are content streams.</summary>
  public bool IsType3 { get; private set; }

  /// <summary>Whether text in this font runs top to bottom (WMode 1).</summary>
  public bool IsVertical => this._cmap?.Vertical == true;

  /// <summary>Why the font's glyphs cannot be drawn, or null when they can.</summary>
  public string? MissingReason { get; private set; }

  /// <summary>Type 3: glyph space to text space.</summary>
  public Matrix2D FontMatrix { get; private set; } = new(0.001, 0, 0, 0.001, 0, 0);

  /// <summary>Type 3: the resources its glyph procedures use.</summary>
  public PdfDict? Resources { get; private set; }

  private IPdfFontProgram? _program;
  private PdfDict? _charProcs;
  private string?[] _encoding = new string?[256];
  private double[] _widths = [];
  private int _firstChar;
  private double _missingWidth;
  private string? _standardName;
  private int _flags;
  private bool _symbolic;

  private PdfCMap? _cmap;
  private int[]? _cidToGid;
  private readonly Dictionary<int, double> _cidWidths = [];
  private double _defaultWidth = 1000;
  private readonly Dictionary<int, (double W1, double Vx, double Vy)> _cidVertical = [];
  private (double Vy, double W1) _defaultVertical = (880, -1000);

  private PdfCidSubstitute? _substitute;
  private readonly PdfSystemFonts? _systemFonts;

  private PdfFont(PdfDocument document, PdfSystemFonts? systemFonts) {
    this._document = document;
    this._systemFonts = systemFonts;
  }

  /// <summary>The installed font drawing a non-embedded CID font, or null.</summary>
  internal PdfCidSubstitute? Substitute => this._substitute;

  /// <summary>Reads a font dictionary; a non-embedded CJK CID font is looked for among the given installed fonts.</summary>
  public static PdfFont Load(PdfDocument document, PdfDict dict, PdfSystemFonts? systemFonts = null) {
    var font = new PdfFont(document, systemFonts);
    font.Name = document.Name(dict, "BaseFont") ?? "unnamed";
    var subtype = document.Name(dict, "Subtype");
    switch (subtype) {
      case "Type0":
        font._LoadComposite(dict);
        break;
      case "Type3":
        font._LoadType3(dict);
        break;
      default:
        font._LoadSimple(dict, subtype ?? "Type1");
        break;
    }

    return font;
  }

  #region simple fonts

  private void _LoadSimple(PdfDict dict, string subtype) {
    var document = this._document;
    var descriptor = document.Dict(dict.Raw("FontDescriptor"));
    this._flags = document.Integer(descriptor, "Flags");
    this._symbolic = (this._flags & 4) != 0 && (this._flags & 32) == 0;
    this._missingWidth = document.Number(descriptor, "MissingWidth");
    this._firstChar = document.Integer(dict, "FirstChar");
    this._widths = document.Numbers(dict.Raw("Widths")) ?? [];

    var baseName = _StripSubset(this.Name);
    this._program = this._LoadProgram(descriptor, false);
    if (this._program == null) {
      var bold = (this._flags & 0x40000) != 0 || baseName.Contains("Bold", StringComparison.OrdinalIgnoreCase);
      var italic = (this._flags & 0x40) != 0 || baseName.Contains("Italic", StringComparison.OrdinalIgnoreCase) || baseName.Contains("Oblique", StringComparison.OrdinalIgnoreCase);
      this._standardName = StandardFontNames.Canonical(baseName, this._flags, bold, italic) ?? StandardFontNames.Fallback(baseName, this._flags, bold, italic);
      if (SubstituteFonts.TrueTypeFor(this._standardName) is { } substitute) {
        this._program = TrueTypeFontProgram.Parse(substitute);
        this.MissingReason = null;
      } else
        this.MissingReason = $"the font {this.Name} is not embedded and no substitute for {this._standardName} is available";
      this._symbolic = this._standardName is "Symbol" or "ZapfDingbats" || this._symbolic;
    }

    // The encoding: a base, then the differences.
    var encoding = document.Resolve(dict.Raw("Encoding"));
    string?[]? base_ = null;
    PdfDict? encodingDict = null;
    if (encoding is string encodingName)
      base_ = PdfEncodings.ByName(encodingName);
    else if (encoding is PdfDict ed) {
      encodingDict = ed;
      if (document.Name(ed, "BaseEncoding") is { } baseEncodingName)
        base_ = PdfEncodings.ByName(baseEncodingName);
    }

    if (base_ == null) {
      if (this._standardName == "Symbol")
        base_ = PdfEncodings.ByName("SymbolSetEncoding");
      else if (this._standardName == "ZapfDingbats")
        base_ = PdfEncodings.ByName("ZapfDingbatsEncoding");
      else if (this._program?.BuiltInEncoding is { } builtin && (subtype != "TrueType") && (this._symbolic || encoding == null || encodingDict != null)) {
        base_ = new string?[256];
        foreach (var (code, name) in builtin)
          if ((uint)code < 256)
            base_[code] = name;
      } else if (subtype != "TrueType" || !this._symbolic)
        base_ = PdfEncodings.ByName("StandardEncoding");
    }

    this._encoding = base_ ?? new string?[256];
    if (document.Array(encodingDict?.Raw("Differences")) is { } differences) {
      var code = 0;
      foreach (var item in differences) {
        var value = document.Resolve(item);
        if (PdfValues.IsNumber(value))
          code = PdfValues.Integer(value);
        else if (value is string name && (uint)code < 256)
          this._encoding[code++] = name;
      }
    }
  }

  private static string _StripSubset(string name) => name.Length > 7 && name[6] == '+' && name[..6].All(char.IsUpper) ? name[7..] : name;

  private IPdfFontProgram? _LoadProgram(PdfDict? descriptor, bool composite) {
    var document = this._document;
    if (descriptor == null)
      return null;

    try {
      if (document.Stream(descriptor.Raw("FontFile2")) is { } ttf)
        return TrueTypeFontProgram.ParseOpenType(document.Decode(ttf));

      if (document.Stream(descriptor.Raw("FontFile3")) is { } cff) {
        var data = document.Decode(cff);
        return document.Name(cff.Dictionary, "Subtype") == "OpenType" ? TrueTypeFontProgram.ParseOpenType(data) : CffFontProgram.Parse(data);
      }

      if (document.Stream(descriptor.Raw("FontFile")) is { } type1) {
        var data = document.Decode(type1);
        // A Type 1 font program sometimes arrives as bare CFF, which viewers accept.
        if (data.Length > 4 && data[0] == 1 && data[1] == 0 && data[2] == 4)
          return CffFontProgram.Parse(data);
        return Type1FontProgram.Parse(data, document.Integer(type1.Dictionary, "Length1"), document.Integer(type1.Dictionary, "Length2"));
      }
    } catch (Exception e) when (e is InvalidDataException or IndexOutOfRangeException or ArgumentException or InvalidCastException or OverflowException) {
      // A broken embedded program draws nothing rather than taking the page down.
      this.MissingReason = $"the embedded program of font {this.Name} could not be read ({e.Message})";
    }

    return null;
  }

  #endregion

  #region composite fonts

  private void _LoadComposite(PdfDict dict) {
    var document = this._document;
    this.IsComposite = true;
    this._cmap = PdfCMap.Parse(document, dict.Raw("Encoding"));

    var descendants = document.Array(dict.Raw("DescendantFonts"));
    var cidFont = descendants is { Count: > 0 } ? document.Dict(descendants[0]) : null;
    if (cidFont == null) {
      this.MissingReason = $"the composite font {this.Name} has no descendant font";
      return;
    }

    var descriptor = document.Dict(cidFont.Raw("FontDescriptor"));
    this._defaultWidth = document.Number(cidFont, "DW", 1000);
    if (document.Array(cidFont.Raw("W")) is { } widths)
      for (var i = 0; i < widths.Count;) {
        var first = PdfValues.Integer(document.Resolve(widths[i]));
        if (i + 1 >= widths.Count)
          break;
        var next = document.Resolve(widths[i + 1]);
        if (next is List<object?> list) {
          for (var k = 0; k < list.Count; ++k)
            this._cidWidths[first + k] = PdfValues.Number(document.Resolve(list[k]));
          i += 2;
        } else {
          if (i + 2 >= widths.Count)
            break;
          var last = PdfValues.Integer(next);
          var w = PdfValues.Number(document.Resolve(widths[i + 2]));
          for (var c = first; c <= last && c - first < 65536; ++c)
            this._cidWidths[c] = w;
          i += 3;
        }
      }

    if (document.Numbers(cidFont.Raw("DW2")) is { Length: 2 } dw2)
      this._defaultVertical = (dw2[0], dw2[1]);
    if (document.Array(cidFont.Raw("W2")) is { } vertical)
      for (var i = 0; i < vertical.Count;) {
        var first = PdfValues.Integer(document.Resolve(vertical[i]));
        if (i + 1 >= vertical.Count)
          break;
        var next = document.Resolve(vertical[i + 1]);
        if (next is List<object?> list) {
          for (var k = 0; k + 2 < list.Count; k += 3)
            this._cidVertical[first + k / 3] = (PdfValues.Number(document.Resolve(list[k])), PdfValues.Number(document.Resolve(list[k + 1])), PdfValues.Number(document.Resolve(list[k + 2])));
          i += 2;
        } else {
          if (i + 4 >= vertical.Count)
            break;
          var last = PdfValues.Integer(next);
          var entry = (PdfValues.Number(document.Resolve(vertical[i + 2])), PdfValues.Number(document.Resolve(vertical[i + 3])), PdfValues.Number(document.Resolve(vertical[i + 4])));
          for (var c = first; c <= last && c - first < 65536; ++c)
            this._cidVertical[c] = entry;
          i += 5;
        }
      }

    this._program = this._LoadProgram(descriptor, true);
    if (this._program == null && this.MissingReason == null) {
      this._substitute = this._FindSubstitute(cidFont, descriptor, out var collection);
      if (this._substitute != null) {
        // The installed face is reached through the collection, never through a CIDToGIDMap.
        this._program = this._substitute.Face.Program;
        return;
      }

      this.MissingReason = collection is "Adobe-Japan1" or "Adobe-GB1" or "Adobe-CNS1" or "Adobe-Korea1"
        ? this._systemFonts == null
          ? $"the CID font {this.Name} is not embedded, and installed fonts are not searched"
          : $"the CID font {this.Name} is not embedded, and no installed font covers {collection}"
        : $"the CID font {this.Name} is not embedded, and its character collection {collection ?? "(none)"} is not one an installed font can stand in for";
    }

    var map = document.Resolve(cidFont.Raw("CIDToGIDMap"));
    if (map is PdfStreamObject mapStream) {
      var bytes = document.Decode(mapStream);
      this._cidToGid = new int[bytes.Length / 2];
      for (var i = 0; i < this._cidToGid.Length; ++i)
        this._cidToGid[i] = (bytes[2 * i] << 8) | bytes[2 * i + 1];
    }
  }

  // A non-embedded CID font of one of Adobe's CJK collections is drawn with an installed font that
  // covers the collection, chosen by the descendant's name, the descriptor's serif, fixed-pitch and
  // bold flags and its weight.
  private PdfCidSubstitute? _FindSubstitute(PdfDict cidFont, PdfDict? descriptor, out string? collection) {
    var document = this._document;
    var info = document.Dict(cidFont.Raw("CIDSystemInfo"));
    var registry = document.Get(info, "Registry") is byte[] r ? System.Text.Encoding.Latin1.GetString(r) : null;
    var ordering = document.Get(info, "Ordering") is byte[] o ? System.Text.Encoding.Latin1.GetString(o) : null;
    collection = registry != null && ordering != null ? registry + "-" + ordering : null;
    if (this._systemFonts == null || registry != "Adobe" || ordering == null || PdfCidToUnicode.For(ordering) is not { } table)
      return null;

    var name = _StripSubset(document.Name(cidFont, "BaseFont") ?? this.Name);
    var comma = name.IndexOf(',');
    var style = comma >= 0 ? name[(comma + 1)..] : "";
    if (comma >= 0)
      name = name[..comma];

    var flags = document.Integer(descriptor, "Flags");
    var serif = PdfFontNames.IsSerif(name) ?? (flags & 2) != 0;
    var bold = PdfFontNames.IsBold(name) || PdfFontNames.IsBold(style) || (flags & 0x40000) != 0 || document.Number(descriptor, "FontWeight") >= 600;
    var face = this._systemFonts.Find(ordering, name, serif, bold, (flags & 1) != 0);
    return face == null ? null : new PdfCidSubstitute(face, table, this.IsVertical);
  }

  #endregion

  #region type 3

  private void _LoadType3(PdfDict dict) {
    var document = this._document;
    this.IsType3 = true;
    if (document.Numbers(dict.Raw("FontMatrix")) is { Length: 6 } m)
      this.FontMatrix = new Matrix2D(m[0], m[1], m[2], m[3], m[4], m[5]);
    this._charProcs = document.Dict(dict.Raw("CharProcs"));
    this.Resources = document.Dict(dict.Raw("Resources"));
    this._firstChar = document.Integer(dict, "FirstChar");
    this._widths = document.Numbers(dict.Raw("Widths")) ?? [];

    var encoding = document.Dict(dict.Raw("Encoding"));
    var base_ = document.Name(encoding, "BaseEncoding") is { } baseName ? PdfEncodings.ByName(baseName) : null;
    this._encoding = base_ ?? new string?[256];
    if (document.Array(encoding?.Raw("Differences")) is { } differences) {
      var code = 0;
      foreach (var item in differences) {
        var value = document.Resolve(item);
        if (PdfValues.IsNumber(value))
          code = PdfValues.Integer(value);
        else if (value is string name && (uint)code < 256)
          this._encoding[code++] = name;
      }
    }
  }

  /// <summary>Type 3: the glyph procedure for a code, or null.</summary>
  public PdfStreamObject? CharProc(int code) {
    var name = (uint)code < 256 ? this._encoding[code] : null;
    return name == null ? null : this._document.Stream(this._charProcs?.Raw(name));
  }

  #endregion

  /// <summary>
  /// Splits a string into character codes — CIDs for a composite font — with whether each is the
  /// single-byte 32 word spacing applies to.
  /// </summary>
  public IEnumerable<(int Code, bool WordSpace)> Codes(byte[] text) {
    if (this._cmap == null) {
      foreach (var b in text)
        yield return (b, b == 32);
      yield break;
    }

    foreach (var (code, length) in this._cmap.Codes(text))
      yield return (this._cmap.Cid(code, length), length == 1 && code == 32);
  }

  /// <summary>The horizontal displacement of a code in text space at size 1 (before Tc, Tw and Tz).</summary>
  public double Width(int code) {
    if (this.IsComposite)
      return (this._cidWidths.TryGetValue(code, out var w) ? w : this._defaultWidth) / 1000;

    var index = code - this._firstChar;
    if (index >= 0 && index < this._widths.Length)
      return this.IsType3 ? this._widths[index] * this.FontMatrix.A : this._widths[index] / 1000;

    if (this.IsType3)
      return 0;

    var name = (uint)code < 256 ? this._encoding[code] : null;
    if (this._standardName != null && name != null && StandardFontMetrics.TryGetWidth(this._standardName, name, out var standard))
      return standard / 1000;
    if (this._missingWidth > 0)
      return this._missingWidth / 1000;
    if (this._program != null && this._GlyphOf(code) is var gid and >= 0 && this._program.AdvanceWidth(gid) is var advance && double.IsFinite(advance))
      return advance;

    return 0;
  }

  /// <summary>Vertical writing: the displacement down the line and the position vector, in text space at size 1.</summary>
  public (double W1, double Vx, double Vy) VerticalMetrics(int cid) {
    if (this._cidVertical.TryGetValue(cid, out var v))
      return (v.W1 / 1000, v.Vx / 1000, v.Vy / 1000);

    return (this._defaultVertical.W1 / 1000, this.Width(cid) / 2, this._defaultVertical.Vy / 1000);
  }

  /// <summary>The outline of a code in text space at size 1, or null when it draws nothing.</summary>
  public GlyphOutline? Outline(int code) {
    if (this._program == null)
      return null;

    var gid = this._GlyphOf(code);
    if (gid < 0)
      return null;

    if (!this._outlines.TryGetValue(gid, out var outline)) {
      try {
        outline = this._substitute != null ? this._substitute.Outline(gid) : this._program.Outline(gid);
      } catch (Exception e) when (e is InvalidDataException or IndexOutOfRangeException or ArgumentException or InvalidOperationException) {
        outline = null;
      }

      this._outlines[gid] = outline;
    }

    return outline;
  }

  private int _GlyphOf(int code) {
    var program = this._program!;
    if (this._substitute != null)
      return this._substitute.GlyphOf(code);
    if (this.IsComposite) {
      var cid = code;
      if (program is CffFontProgram { IsCidKeyed: true } cidCff)
        return cidCff.GlyphIdOfCid(cid);
      if (this._cidToGid != null)
        return cid < this._cidToGid.Length ? this._cidToGid[cid] : 0;
      return cid;
    }

    var name = (uint)code < 256 ? this._encoding[code] : null;
    if (program is TrueTypeFontProgram ttf)
      return this._TrueTypeGlyph(ttf, code, name);

    if (name != null) {
      var gid = program.GlyphIdOfName(name);
      if (gid >= 0)
        return gid;

      // A name the program does not know may still be reachable through its Unicode value under
      // another of the standard names.
      var unicode = GlyphList.UnicodeOf(name);
      if (unicode >= 0 && GlyphList.NameOf(unicode) is { } alias && alias != name && program.GlyphIdOfName(alias) is var aliased and >= 0)
        return aliased;
    }

    // A CFF font used as a simple font with a builtin encoding that names nothing: the code is the glyph.
    if (program is CffFontProgram { IsCidKeyed: true } cff)
      return cff.GlyphIdOfCid(code);

    return 0;
  }

  private int _TrueTypeGlyph(TrueTypeFontProgram ttf, int code, string? name) {
    int gid;
    var substitute = this._standardName != null;
    if (!this._symbolic || substitute) {
      if (ttf.HasCmap(3, 1) && name != null) {
        var unicode = GlyphList.UnicodeOf(name);
        if (unicode >= 0 && (gid = ttf.CmapLookup(3, 1, unicode)) > 0)
          return gid;
      }

      if (ttf.HasCmap(1, 0) && name != null) {
        var mac = PdfEncodings.ByName("MacRomanEncoding");
        if (mac != null) {
          var macCode = Array.IndexOf(mac, name);
          if (macCode >= 0 && (gid = ttf.CmapLookup(1, 0, macCode)) > 0)
            return gid;
        }
      }
    }

    if (ttf.HasCmap(3, 0)) {
      foreach (var candidate in new[] { code, 0xF000 + code, 0xF100 + code, 0xF200 + code })
        if ((gid = ttf.CmapLookup(3, 0, candidate)) > 0)
          return gid;
    }

    if (ttf.HasCmap(1, 0) && (gid = ttf.CmapLookup(1, 0, code)) > 0)
      return gid;

    if (ttf.HasCmap(3, 1) && (this._symbolic || name == null) && (gid = ttf.CmapLookup(3, 1, code)) > 0)
      return gid;

    if (name != null && (gid = ttf.GlyphIdOfName(name)) > 0)
      return gid;

    // A subset with no usable cmap maps codes straight to glyph indices.
    return substitute ? 0 : code < ttf.GlyphCount ? code : 0;
  }
}

file static class _StringExtensions {
  public static bool All(this string text, Func<char, bool> predicate) {
    foreach (var c in text)
      if (!predicate(c))
        return false;
    return true;
  }
}
