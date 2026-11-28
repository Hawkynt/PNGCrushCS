using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FileFormat.Pdf.Fonts;

/// <summary>
/// A bare Compact Font Format program — what a PDF embeds as FontFile3 with subtype Type1C or
/// CIDFontType0C, and what an OpenType font carries in its 'CFF ' table.
/// </summary>
/// <remarks>
/// The structure follows the CFF specification (Adobe Technical Note #5176) and the way pdf.js's
/// <c>cff_parser.js</c> reads it, including its tolerance for the damage real files carry: a header
/// that does not start at byte zero, INDEX offsets that run past the data, an FDSelect whose first
/// range does not start at glyph zero. Only what drawing needs is kept — the INDEXes, the charset,
/// the encoding, and per font DICT the matrix, the local subroutines and the two width defaults.
/// Charstrings are interpreted the first time their glyph is asked for, never up front.
/// </remarks>
internal sealed class CffFontProgram : IPdfFontProgram {

  // DICT operators, two-byte ones as 12 << 8 | second byte.
  private const int _OpCharset = 15;
  private const int _OpEncoding = 16;
  private const int _OpCharStrings = 17;
  private const int _OpPrivate = 18;
  private const int _OpSubrs = 19;
  private const int _OpDefaultWidthX = 20;
  private const int _OpNominalWidthX = 21;
  private const int _OpCharstringType = (12 << 8) | 6;
  private const int _OpFontMatrix = (12 << 8) | 7;
  private const int _OpRos = (12 << 8) | 30;
  private const int _OpFdArray = (12 << 8) | 36;
  private const int _OpFdSelect = (12 << 8) | 37;

  /// <summary>CFF FDSelect stores Card8 indices, so no more than this many font DICTs can be addressed.</summary>
  private const int _MaxFdCount = 256;

  private static readonly double[] _DefaultMatrix = [0.001, 0, 0, 0.001, 0, 0];

  private readonly byte[] _data;
  private readonly CffIndex _charStrings;
  private readonly CffIndex _globalSubrs;
  private readonly CffFontDict[] _fonts;
  private readonly byte[]? _fdSelect;
  private readonly string[]? _glyphNames;
  private readonly int[]? _glyphCids;
  private readonly Dictionary<string, int> _gidByName = new(StringComparer.Ordinal);
  private readonly Dictionary<int, int> _gidByCid = [];
  private readonly Dictionary<int, string>? _encoding;
  private readonly GlyphOutline?[] _outlines;
  private readonly double[] _widths;
  private readonly bool[] _parsed;
  private readonly bool _type1Charstrings;
  private readonly Dictionary<CffFontDict, byte[]?[]> _type1Subrs = [];

  private CffFontProgram(byte[] data, CffIndex charStrings, CffIndex globalSubrs, CffFontDict[] fonts, byte[]? fdSelect, string[]? glyphNames, int[]? glyphCids, Dictionary<int, string>? encoding, bool type1Charstrings) {
    this._data = data;
    this._type1Charstrings = type1Charstrings;
    this._charStrings = charStrings;
    this._globalSubrs = globalSubrs;
    this._fonts = fonts;
    this._fdSelect = fdSelect;
    this._glyphNames = glyphNames;
    this._glyphCids = glyphCids;
    this._encoding = encoding;

    var count = charStrings.Count;
    this._outlines = new GlyphOutline?[count];
    this._widths = new double[count];
    this._parsed = new bool[count];

    if (glyphNames != null)
      for (var gid = 0; gid < glyphNames.Length; ++gid)
        this._gidByName.TryAdd(glyphNames[gid], gid);

    if (glyphCids != null)
      for (var gid = 0; gid < glyphCids.Length; ++gid)
        this._gidByCid.TryAdd(glyphCids[gid], gid);
  }

  /// <summary>Whether the font is CID-keyed: glyphs are found by CID rather than by name.</summary>
  public bool IsCidKeyed => this._glyphCids != null;

  /// <inheritdoc/>
  public int GlyphCount => this._charStrings.Count;

  /// <inheritdoc/>
  public IReadOnlyDictionary<int, string>? BuiltInEncoding => this._encoding;

  /// <summary>The glyph id holding a CID, or -1; only a CID-keyed font has any.</summary>
  public int GlyphIdOfCid(int cid) => this._gidByCid.TryGetValue(cid, out var gid) ? gid : -1;

  /// <inheritdoc/>
  public int GlyphIdOfName(string name) => this._gidByName.TryGetValue(name, out var gid) ? gid : -1;

  /// <summary>The name the charset gives a glyph, or null for a CID-keyed font or an id out of range.</summary>
  public string? GlyphName(int glyphId) => this._glyphNames != null && (uint)glyphId < (uint)this._glyphNames.Length ? this._glyphNames[glyphId] : null;

  /// <inheritdoc/>
  public GlyphOutline? Outline(int glyphId) {
    if ((uint)glyphId >= (uint)this._charStrings.Count)
      return null;

    this._Ensure(glyphId);
    return this._outlines[glyphId];
  }

  /// <inheritdoc/>
  public double AdvanceWidth(int glyphId) {
    if ((uint)glyphId >= (uint)this._charStrings.Count)
      return double.NaN;

    this._Ensure(glyphId);
    return this._widths[glyphId];
  }

  /// <summary>Reads a CFF program.</summary>
  /// <exception cref="InvalidDataException">The data is not a CFF font.</exception>
  /// <exception cref="NotSupportedException">The font declares a charstring type other than 1 or 2.</exception>
  public static CffFontProgram Parse(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    // Some producers put bytes in front of the header; the major version is 1, so skip to it.
    var shift = 0;
    while (shift < data.Length && data[shift] != 1)
      ++shift;
    if (shift + 4 > data.Length)
      throw new InvalidDataException("Not a CFF font: no header.");
    if (shift > 0)
      data = data.AsSpan(shift).ToArray();

    var headerSize = data[2];
    var names = CffIndex.Read(data, headerSize);
    var topDicts = CffIndex.Read(data, names.End);
    var stringIndex = CffIndex.Read(data, topDicts.End);
    var globalSubrs = CffIndex.Read(data, stringIndex.End);
    if (topDicts.Count < 1)
      throw new InvalidDataException("CFF font has no Top DICT.");

    var strings = new string[stringIndex.Count];
    for (var i = 0; i < strings.Length; ++i)
      strings[i] = Encoding.Latin1.GetString(stringIndex.Get(data, i));

    var top = ParseDict(topDicts.Get(data, 0));
    // CharstringType 1: the charstrings and subroutines are Type 1 ones, unencrypted (CFF has no
    // lenIV), run by the Type 1 interpreter (Technical Note #5176, Top DICT CharstringType).
    var charstringType = _Number(top, _OpCharstringType, 2);
    if (charstringType is not (1 or 2))
      throw new NotSupportedException($"CFF fonts with CharstringType {charstringType} are not supported.");

    var charStringsOffset = (int)_Number(top, _OpCharStrings, 0);
    if (charStringsOffset <= 0 || charStringsOffset >= data.Length)
      throw new InvalidDataException("CFF font has no CharStrings INDEX.");

    var charStrings = CffIndex.Read(data, charStringsOffset);
    var glyphCount = charStrings.Count;

    var topMatrix = top.TryGetValue(_OpFontMatrix, out var tm) && tm.Length >= 6 ? tm[..6] : null;
    var isCid = top.ContainsKey(_OpRos);

    CffFontDict[] fonts;
    byte[]? fdSelect = null;
    string[]? glyphNames = null;
    int[]? glyphCids = null;
    Dictionary<int, string>? encoding = null;

    if (isCid) {
      var fdArrayOffset = (int)_Number(top, _OpFdArray, 0);
      var fdArray = fdArrayOffset > 0 && fdArrayOffset < data.Length ? CffIndex.Read(data, fdArrayOffset) : CffIndex.Empty;
      var fdCount = Math.Min(fdArray.Count, _MaxFdCount);
      fonts = new CffFontDict[Math.Max(fdCount, 1)];
      for (var i = 0; i < fdCount; ++i) {
        var fd = ParseDict(fdArray.Get(data, i));
        var fdMatrix = fd.TryGetValue(_OpFontMatrix, out var fm) && fm.Length >= 6 ? fm[..6] : null;
        fonts[i] = _ReadPrivate(data, fd, _CombineMatrices(fdMatrix, topMatrix));
      }
      if (fdCount == 0)
        fonts[0] = _ReadPrivate(data, top, topMatrix ?? _DefaultMatrix);

      var fdSelectOffset = (int)_Number(top, _OpFdSelect, 0);
      if (fdSelectOffset > 0 && fdSelectOffset < data.Length)
        fdSelect = _ReadFdSelect(data, fdSelectOffset, glyphCount);

      glyphCids = _ReadCidCharset(data, (int)_Number(top, _OpCharset, 0), glyphCount);
    } else {
      fonts = [_ReadPrivate(data, top, topMatrix ?? _DefaultMatrix)];
      glyphNames = _ReadNameCharset(data, (int)_Number(top, _OpCharset, 0), glyphCount, strings);
      encoding = _ReadEncoding(data, (int)_Number(top, _OpEncoding, 0), glyphNames, strings);
    }

    return new(data, charStrings, globalSubrs, fonts, fdSelect, glyphNames, glyphCids, encoding, charstringType == 1);
  }

  /// <summary>Parses a DICT into its operators and their operands.</summary>
  internal static Dictionary<int, double[]> ParseDict(ReadOnlySpan<byte> dict) {
    var entries = new Dictionary<int, double[]>();
    var operands = new List<double>();
    var pos = 0;
    while (pos < dict.Length) {
      var b = dict[pos];
      if (b <= 21) {
        int op = b;
        ++pos;
        if (b == 12) {
          if (pos >= dict.Length)
            break;
          op = (12 << 8) | dict[pos++];
        }
        entries[op] = operands.ToArray();
        operands.Clear();
        continue;
      }

      ++pos;
      switch (b) {
        case 28:
          if (pos + 2 > dict.Length)
            return entries;
          operands.Add((short)((dict[pos] << 8) | dict[pos + 1]));
          pos += 2;
          break;
        case 29:
          if (pos + 4 > dict.Length)
            return entries;
          operands.Add((dict[pos] << 24) | (dict[pos + 1] << 16) | (dict[pos + 2] << 8) | dict[pos + 3]);
          pos += 4;
          break;
        case 30:
          operands.Add(_ReadReal(dict, ref pos));
          break;
        case >= 32 and <= 246:
          operands.Add(b - 139);
          break;
        case >= 247 and <= 250:
          if (pos >= dict.Length)
            return entries;
          operands.Add((b - 247) * 256 + dict[pos++] + 108);
          break;
        case >= 251 and <= 254:
          if (pos >= dict.Length)
            return entries;
          operands.Add(-(b - 251) * 256 - dict[pos++] - 108);
          break;
        default:
          // Reserved; pdf.js records NaN and carries on, which keeps the operand count right.
          operands.Add(double.NaN);
          break;
      }
    }

    return entries;
  }

  // ---------------------------------------------------------------------------------------------
  // Glyph interpretation
  // ---------------------------------------------------------------------------------------------

  private void _Ensure(int glyphId) {
    if (this._parsed[glyphId])
      return;

    this._parsed[glyphId] = true;
    var font = this._FontOf(glyphId);
    try {
      var outline = new GlyphOutline();
      var width = this._Interpret(glyphId, outline, 0, 0, 0);
      var m = font.Matrix;
      this._outlines[glyphId] = outline.Transformed(m[0], m[1], m[2], m[3], m[4], m[5]);
      this._widths[glyphId] = width * m[0];
    } catch (Exception ex) when (ex is InvalidDataException or IndexOutOfRangeException or ArgumentOutOfRangeException or InvalidOperationException) {
      // A charstring that cannot be run draws nothing, which is what FreeType does with one too.
      this._outlines[glyphId] = new GlyphOutline();
      this._widths[glyphId] = font.DefaultWidthX * font.Matrix[0];
    }
  }

  private CffFontDict _FontOf(int glyphId) {
    if (this._fdSelect == null || this._fonts.Length == 1)
      return this._fonts[0];

    var fd = (uint)glyphId < (uint)this._fdSelect.Length ? this._fdSelect[glyphId] : 0;
    return fd < this._fonts.Length ? this._fonts[fd] : this._fonts[0];
  }

  /// <summary>Runs a glyph's charstring into an outline in font units, returning its advance width.</summary>
  private double _Interpret(int glyphId, GlyphOutline outline, double offsetX, double offsetY, int seacDepth) {
    var font = this._FontOf(glyphId);
    var code = this._charStrings.Get(this._data, glyphId);
    if (this._type1Charstrings)
      return this._InterpretType1(font, code.ToArray(), glyphId, outline, offsetX, offsetY, seacDepth);

    var interpreter = new CffCharStringInterpreter(this._data, this._globalSubrs, font.Subrs, font.DefaultWidthX, font.NominalWidthX, outline, offsetX, offsetY);
    interpreter.Run(code);

    if (interpreter.Seac is { } seac && seacDepth == 0) {
      var (adx, ady, bchar, achar) = seac;
      var baseGid = this._GlyphOfStandardCode(bchar);
      var accentGid = this._GlyphOfStandardCode(achar);
      if (baseGid >= 0)
        this._Interpret(baseGid, outline, offsetX, offsetY, seacDepth + 1);
      if (accentGid >= 0)
        this._Interpret(accentGid, outline, offsetX + adx, offsetY + ady, seacDepth + 1);
    }

    outline.Close();
    return interpreter.Width;
  }

  /// <summary>A Type 1 charstring inside CFF, with the font DICT's Subrs INDEX as its subroutines.</summary>
  private double _InterpretType1(CffFontDict font, byte[] code, int glyphId, GlyphOutline outline, double offsetX, double offsetY, int seacDepth) {
    if (!this._type1Subrs.TryGetValue(font, out var subrs)) {
      subrs = new byte[]?[font.Subrs.Count];
      for (var i = 0; i < subrs.Length; ++i)
        subrs[i] = font.Subrs.Get(this._data, i).ToArray();
      this._type1Subrs[font] = subrs;
    }

    var interpreter = new Type1CharStringInterpreter(subrs, outline, offsetX, offsetY);
    interpreter.Run(code);
    if (interpreter.Seac is { } seac && seacDepth == 0) {
      var baseGid = this._GlyphOfStandardCode(seac.BaseCode);
      var accentGid = this._GlyphOfStandardCode(seac.AccentCode);
      if (baseGid >= 0)
        this._Interpret(baseGid, outline, offsetX, offsetY, seacDepth + 1);
      if (accentGid >= 0)
        this._Interpret(accentGid, outline, offsetX + seac.Adx, offsetY + seac.Ady, seacDepth + 1);
    }

    outline.Close();
    return interpreter.Width;
  }

  private int _GlyphOfStandardCode(int code) {
    var name = Type1StandardEncoding.NameOf(code);
    return name == null ? -1 : this.GlyphIdOfName(name);
  }

  // ---------------------------------------------------------------------------------------------
  // Structure
  // ---------------------------------------------------------------------------------------------

  private static CffFontDict _ReadPrivate(byte[] data, Dictionary<int, double[]> dict, double[] matrix) {
    var subrs = CffIndex.Empty;
    double defaultWidth = 0, nominalWidth = 0;
    if (dict.TryGetValue(_OpPrivate, out var priv) && priv.Length >= 2) {
      var size = (int)priv[0];
      var offset = (int)priv[1];
      if (size > 0 && offset > 0 && offset < data.Length) {
        size = Math.Min(size, data.Length - offset);
        var privateDict = ParseDict(data.AsSpan(offset, size));
        defaultWidth = _Number(privateDict, _OpDefaultWidthX, 0);
        nominalWidth = _Number(privateDict, _OpNominalWidthX, 0);
        var subrsOffset = (int)_Number(privateDict, _OpSubrs, 0);
        if (subrsOffset > 0 && offset + subrsOffset < data.Length)
          subrs = CffIndex.Read(data, offset + subrsOffset);
      }
    }

    return new(matrix, subrs, defaultWidth, nominalWidth);
  }

  // A font DICT's matrix applies within the Top DICT's, the default of which is the plain
  // thousand-unit em; FreeType combines the two the same way.
  private static double[] _CombineMatrices(double[]? fd, double[]? top) {
    if (fd == null)
      return top ?? _DefaultMatrix;
    if (top == null)
      return fd;

    var t = new double[6];
    for (var i = 0; i < 6; ++i)
      t[i] = top[i] * 1000;

    return [
      fd[0] * t[0] + fd[1] * t[2],
      fd[0] * t[1] + fd[1] * t[3],
      fd[2] * t[0] + fd[3] * t[2],
      fd[2] * t[1] + fd[3] * t[3],
      fd[4] * t[0] + fd[5] * t[2] + t[4] / 1000,
      fd[4] * t[1] + fd[5] * t[3] + t[5] / 1000,
    ];
  }

  private static string[] _ReadNameCharset(byte[] data, int offset, int glyphCount, string[] strings) {
    var names = new string[glyphCount];
    if (glyphCount == 0)
      return names;

    string[]? predefined = offset switch {
      0 => CffCharsets.IsoAdobe,
      1 => CffCharsets.Expert,
      2 => CffCharsets.ExpertSubset,
      _ => null,
    };
    if (predefined != null) {
      for (var gid = 0; gid < glyphCount; ++gid)
        names[gid] = gid < predefined.Length ? predefined[gid] : $"glyph{gid}";
      return names;
    }

    var sids = _ReadCharsetIds(data, offset, glyphCount);
    for (var gid = 0; gid < glyphCount; ++gid)
      names[gid] = gid == 0 ? ".notdef" : sids[gid] >= 0 ? _String(sids[gid], strings) : $"glyph{gid}";

    return names;
  }

  private static int[] _ReadCidCharset(byte[] data, int offset, int glyphCount) {
    // A CID font with a predefined charset makes no sense, but some producers write one; the
    // identity reading of it is what every viewer falls back on.
    if (offset is 0 or 1 or 2) {
      var identity = new int[glyphCount];
      for (var gid = 0; gid < glyphCount; ++gid)
        identity[gid] = gid;
      return identity;
    }

    var cids = _ReadCharsetIds(data, offset, glyphCount);
    for (var gid = 0; gid < glyphCount; ++gid)
      if (cids[gid] < 0)
        cids[gid] = gid == 0 ? 0 : -1;

    return cids;
  }

  // Charset formats 0, 1 and 2: the ids of glyphs 1 onward, glyph 0 always being .notdef / CID 0.
  private static int[] _ReadCharsetIds(byte[] data, int pos, int glyphCount) {
    var ids = new int[glyphCount];
    Array.Fill(ids, -1);
    if (glyphCount == 0 || pos <= 0 || pos >= data.Length)
      return ids;

    ids[0] = 0;
    var format = data[pos++];
    var gid = 1;
    switch (format) {
      case 0:
        while (gid < glyphCount && pos + 2 <= data.Length) {
          ids[gid++] = (data[pos] << 8) | data[pos + 1];
          pos += 2;
        }
        break;
      case 1:
      case 2:
        var countSize = format == 1 ? 1 : 2;
        while (gid < glyphCount && pos + 2 + countSize <= data.Length) {
          var first = (data[pos] << 8) | data[pos + 1];
          var left = countSize == 1 ? data[pos + 2] : (data[pos + 2] << 8) | data[pos + 3];
          pos += 2 + countSize;
          for (var i = 0; i <= left && gid < glyphCount; ++i)
            ids[gid++] = first + i;
        }
        break;
      default:
        throw new InvalidDataException($"Unknown CFF charset format {format}.");
    }

    return ids;
  }

  private static Dictionary<int, string> _ReadEncoding(byte[] data, int offset, string[] glyphNames, string[] strings) {
    var encoding = new Dictionary<int, string>();
    if (offset is 0 or 1) {
      // The predefined encodings name glyphs, and a code is mapped when the font has that glyph.
      var present = new HashSet<string>(glyphNames, StringComparer.Ordinal);
      for (var code = 0; code < 256; ++code) {
        var name = offset == 0 ? Type1StandardEncoding.NameOf(code) : CffCharsets.ExpertEncoding[code] is { Length: > 0 } n ? n : null;
        if (name != null && present.Contains(name))
          encoding[code] = name;
      }
      return encoding;
    }

    if (offset >= data.Length)
      return encoding;

    var pos = offset;
    var format = data[pos++];
    switch (format & 0x7F) {
      case 0: {
        var count = pos < data.Length ? data[pos++] : 0;
        for (var gid = 1; gid <= count && pos < data.Length; ++gid) {
          var code = data[pos++];
          // Writers fill the code of a glyph they leave unencoded with zero; nothing is mapped there.
          if (code != 0 && gid < glyphNames.Length)
            encoding[code] = glyphNames[gid];
        }
        break;
      }
      case 1: {
        var ranges = pos < data.Length ? data[pos++] : 0;
        var gid = 1;
        for (var r = 0; r < ranges && pos + 2 <= data.Length; ++r) {
          var first = data[pos];
          var left = data[pos + 1];
          pos += 2;
          for (int code = first; code <= first + left; ++code, ++gid)
            if (gid < glyphNames.Length && code < 256)
              encoding[code] = glyphNames[gid];
        }
        break;
      }
      default:
        throw new InvalidDataException($"Unknown CFF encoding format {format & 0x7F}.");
    }

    if ((format & 0x80) != 0 && pos < data.Length) {
      var supplements = data[pos++];
      for (var i = 0; i < supplements && pos + 3 <= data.Length; ++i) {
        var code = data[pos];
        var sid = (data[pos + 1] << 8) | data[pos + 2];
        pos += 3;
        encoding[code] = _String(sid, strings);
      }
    }

    return encoding;
  }

  private static byte[] _ReadFdSelect(byte[] data, int pos, int glyphCount) {
    var select = new byte[glyphCount];
    var format = data[pos++];
    switch (format) {
      case 0:
        for (var gid = 0; gid < glyphCount && pos < data.Length; ++gid)
          select[gid] = data[pos++];
        break;
      case 3: {
        if (pos + 2 > data.Length)
          break;
        var ranges = (data[pos] << 8) | data[pos + 1];
        pos += 2;
        for (var r = 0; r < ranges && pos + 5 <= data.Length; ++r) {
          var first = (data[pos] << 8) | data[pos + 1];
          if (r == 0)
            first = 0; // the first range must start at glyph zero; pdf.js recovers the same way
          var fd = data[pos + 2];
          var next = (data[pos + 3] << 8) | data[pos + 4];
          pos += 3;
          for (var gid = first; gid < next && gid < glyphCount; ++gid)
            select[gid] = fd;
        }
        break;
      }
      default:
        throw new InvalidDataException($"Unknown CFF FDSelect format {format}.");
    }

    return select;
  }

  private static string _String(int sid, string[] strings) {
    if (sid < CffStandardStrings.Count)
      return CffStandardStrings.Names[sid];

    var index = sid - CffStandardStrings.Count;
    return index < strings.Length ? strings[index] : $"sid{sid}";
  }

  private static double _Number(Dictionary<int, double[]> dict, int op, double fallback)
    => dict.TryGetValue(op, out var values) && values.Length > 0 && double.IsFinite(values[0]) ? values[0] : fallback;

  private static double _ReadReal(ReadOnlySpan<byte> dict, ref int pos) {
    var text = new StringBuilder();
    while (pos < dict.Length) {
      var b = dict[pos++];
      var done = false;
      foreach (var nibble in (ReadOnlySpan<int>)[b >> 4, b & 15]) {
        switch (nibble) {
          case <= 9:
            text.Append((char)('0' + nibble));
            break;
          case 10:
            text.Append('.');
            break;
          case 11:
            text.Append('E');
            break;
          case 12:
            text.Append("E-");
            break;
          case 14:
            text.Append('-');
            break;
          case 15:
            done = true;
            break;
        }

        if (done)
          break;
      }

      if (done)
        break;
    }

    return double.TryParse(text.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
  }
}

/// <summary>What one font DICT contributes to drawing its glyphs.</summary>
internal sealed record CffFontDict(double[] Matrix, CffIndex Subrs, double DefaultWidthX, double NominalWidthX);

/// <summary>A CFF INDEX: a count of objects and where in the font each one lies.</summary>
internal sealed class CffIndex {

  private readonly int[] _offsets;

  /// <summary>An INDEX with nothing in it.</summary>
  public static CffIndex Empty { get; } = new([], 0);

  private CffIndex(int[] offsets, int end) {
    this._offsets = offsets;
    this.End = end;
  }

  /// <summary>How many objects it holds.</summary>
  public int Count => Math.Max(this._offsets.Length - 1, 0);

  /// <summary>The first byte after the INDEX.</summary>
  public int End { get; }

  /// <summary>One object's bytes; out-of-range indices give nothing.</summary>
  public ReadOnlySpan<byte> Get(byte[] data, int index) {
    if ((uint)index >= (uint)this.Count)
      return [];

    var start = this._offsets[index];
    var end = this._offsets[index + 1];
    return end > start ? data.AsSpan(start, end - start) : [];
  }

  /// <summary>Reads an INDEX at a position, clamping offsets that run outside the data.</summary>
  public static CffIndex Read(byte[] data, int pos) {
    if (pos < 0 || pos + 2 > data.Length)
      return new([], Math.Min(Math.Max(pos, 0), data.Length));

    var count = (data[pos] << 8) | data[pos + 1];
    pos += 2;
    if (count == 0)
      return new([], pos);

    if (pos >= data.Length)
      throw new InvalidDataException("CFF INDEX is truncated.");

    var offSize = data[pos++];
    if (offSize is < 1 or > 4)
      throw new InvalidDataException($"Invalid CFF INDEX offset size {offSize}.");

    // Offsets count from the byte before the object data.
    var dataStart = pos + (count + 1) * offSize - 1;
    var offsets = new int[count + 1];
    var previous = Math.Min(dataStart, data.Length);
    for (var i = 0; i <= count; ++i) {
      long offset = 0;
      for (var j = 0; j < offSize; ++j)
        offset = (offset << 8) | (uint)(pos < data.Length ? data[pos++] : 0);

      previous = (int)Math.Clamp(dataStart + offset, previous, data.Length);
      offsets[i] = previous;
    }

    return new(offsets, offsets[count]);
  }
}
