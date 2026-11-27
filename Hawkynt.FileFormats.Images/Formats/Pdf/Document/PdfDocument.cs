using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Pdf.Document;

/// <summary>
/// A PDF file opened for reading: its cross-reference, its objects on demand, and its page tree.
/// </summary>
/// <remarks>
/// Written from PDF 32000-1 §7.5 (file structure) and §7.7.3 (the page tree). The cross-reference is
/// read newest first along the <c>/Prev</c> chain, so an incremental update's entries win over the
/// ones it replaces; tables, cross-reference streams and hybrid files with <c>/XRefStm</c> are all
/// followed, and objects stored in object streams are found through them. When the cross-reference
/// is missing or points into nowhere — which is common enough that every viewer copes — the file is
/// scanned for <c>n g obj</c> headers instead and the last definition of each object wins, which is
/// what an incremental update means.
/// </remarks>
internal sealed class PdfDocument {

  private readonly record struct XrefEntry(long Offset, int StreamNumber, int Index, int Generation) {
    public bool Compressed => this.StreamNumber >= 0;
  }

  private readonly byte[] _data;
  private readonly Dictionary<int, XrefEntry> _xref = [];
  private readonly Dictionary<int, object?> _cache = [];
  private readonly HashSet<int> _resolving = [];
  private readonly Dictionary<int, (int[] Numbers, int[] Offsets, int First, byte[] Data)> _objectStreams = [];
  private int _headerOffset;
  private PdfSecurityHandler? _security;
  private int _encryptObject = -1;
  private bool _reconstructed;

  /// <summary>The trailer dictionary (for a cross-reference stream, its dictionary).</summary>
  public PdfDict Trailer { get; private set; } = new();

  /// <summary>The document catalog.</summary>
  public PdfDict Catalog { get; private set; } = new();

  /// <summary>Every page, in order.</summary>
  public IReadOnlyList<PdfPageNode> Pages { get; private set; } = [];

  /// <summary>Whether the file was encrypted.</summary>
  public bool IsEncrypted => this._security != null;

  private PdfDocument(byte[] data) => this._data = data;

  /// <summary>Opens a PDF held in memory.</summary>
  /// <exception cref="InvalidDataException">The bytes are not a PDF that can be read.</exception>
  public static PdfDocument Open(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    var document = new PdfDocument(data);
    document._Load();
    return document;
  }

  private void _Load() {
    this._headerOffset = _FindHeader(this._data);
    if (this._headerOffset < 0)
      throw new InvalidDataException("Invalid PDF signature.");

    var ok = false;
    try {
      ok = this._ReadCrossReference() && this._LoadCatalog();
    } catch (Exception e) when (e is InvalidDataException or IndexOutOfRangeException or ArgumentException or InvalidCastException or OverflowException) {
      ok = false;
    }

    if (!ok) {
      this._xref.Clear();
      this._cache.Clear();
      this._objectStreams.Clear();
      this._Reconstruct();
      if (!this._LoadCatalog())
        throw new InvalidDataException("The PDF has no readable document catalog.");
    }

    this._OpenEncryption();
    this.Pages = this._CollectPages();
    if (this.Pages.Count == 0 && !this._reconstructed) {
      // A tree that leads nowhere can still be rebuilt from the objects themselves.
      this._reconstructed = true;
      this.Pages = this._ScanForPages();
    }
  }

  private static int _FindHeader(byte[] data) {
    var limit = Math.Min(data.Length - 5, 1024);
    for (var i = 0; i <= limit; ++i)
      if (data[i] == (byte)'%' && data[i + 1] == (byte)'P' && data[i + 2] == (byte)'D' && data[i + 3] == (byte)'F' && data[i + 4] == (byte)'-')
        return i;

    return -1;
  }

  #region cross-reference

  private bool _ReadCrossReference() {
    var start = _FindLast(this._data, "startxref"u8);
    if (start < 0)
      return false;

    var lexer = new PdfLexer(this._data, start + 9);
    if (lexer.ReadObject() is not { } offsetValue || !PdfValues.IsNumber(offsetValue))
      return false;

    var offset = (long)PdfValues.Number(offsetValue);
    var visited = new HashSet<long>();
    var first = true;
    while (offset > 0 || first) {
      if (!visited.Add(offset))
        break;

      var trailer = this._ReadSection(offset) ?? this._ReadSection(offset + this._headerOffset);
      if (trailer == null)
        return false;

      if (first) {
        this.Trailer = trailer;
        first = false;
      } else
        foreach (var (key, value) in trailer)
          this.Trailer.TryAdd(key, value);

      // A hybrid file keeps the entries a 1.5 reader needs in a stream beside the table.
      if (trailer.Raw("XRefStm") is { } stm && PdfValues.IsNumber(stm))
        this._ReadSection((long)PdfValues.Number(stm));

      var prev = trailer.Raw("Prev");
      if (!PdfValues.IsNumber(prev))
        break;

      offset = (long)PdfValues.Number(prev);
    }

    return this.Trailer.Count > 0;
  }

  private PdfDict? _ReadSection(long offset) {
    if (offset < 0 || offset >= this._data.Length)
      return null;

    var lexer = new PdfLexer(this._data, (int)offset);
    lexer.SkipWhitespace();
    if (PdfLexer.MatchesKeyword(this._data, lexer.Position, "xref"u8))
      return this._ReadTable(lexer);

    return this._ReadXrefStream((int)offset);
  }

  private PdfDict? _ReadTable(PdfLexer lexer) {
    lexer.Position += 4;
    while (true) {
      lexer.SkipWhitespace();
      if (lexer.Position >= this._data.Length)
        return null;
      if (PdfLexer.MatchesKeyword(this._data, lexer.Position, "trailer"u8)) {
        lexer.Position += 7;
        return lexer.ReadObject() as PdfDict;
      }

      if (lexer.ReadObject() is not int firstObject || lexer.ReadObject() is not int count || count < 0)
        return null;

      for (var i = 0; i < count; ++i) {
        // Each entry is "offset generation type", nominally twenty bytes; reading it as three tokens
        // copes with the writers that get the line ending wrong.
        if (lexer.ReadObject() is not { } off || lexer.ReadObject() is not int generation || lexer.ReadObject() is not PdfOperator type)
          return null;

        var number = firstObject + i;
        if (type.Name == "n" && PdfValues.IsNumber(off) && !this._xref.ContainsKey(number))
          this._xref[number] = new((long)PdfValues.Number(off), -1, 0, generation);
        else if (type.Name == "f")
          this._xref.TryAdd(number, new(-1, -1, 0, generation));
      }
    }
  }

  private PdfDict? _ReadXrefStream(int offset) {
    var parsed = this._ParseIndirectAt(offset, -1, out _);
    if (parsed is not PdfStreamObject stream || stream.Dictionary.Raw("Type") as string != "XRef")
      return null;

    var dict = stream.Dictionary;
    var data = PdfFilters.Decode(this, dict, stream.Data, out _, out _);
    if (this.Resolve(dict.Raw("W")) is not List<object?> w || w.Count < 3)
      return null;

    var w0 = PdfValues.Integer(w[0]);
    var w1 = PdfValues.Integer(w[1]);
    var w2 = PdfValues.Integer(w[2]);
    var entry = w0 + w1 + w2;
    if (entry <= 0 || w0 < 0 || w1 < 0 || w2 < 0)
      return null;

    var index = this.Resolve(dict.Raw("Index")) as List<object?> ?? [0, dict.Raw("Size")];
    var at = 0;
    for (var p = 0; p + 1 < index.Count; p += 2) {
      var first = PdfValues.Integer(index[p]);
      var count = PdfValues.Integer(index[p + 1]);
      for (var i = 0; i < count && at + entry <= data.Length; ++i, at += entry) {
        var type = w0 == 0 ? 1 : (int)_BigEndian(data, at, w0);
        var field2 = _BigEndian(data, at + w0, w1);
        var field3 = _BigEndian(data, at + w0 + w1, w2);
        var number = first + i;
        if (this._xref.ContainsKey(number))
          continue;

        switch (type) {
          case 0:
            this._xref[number] = new(-1, -1, 0, (int)field3);
            break;
          case 1:
            this._xref[number] = new(field2, -1, 0, (int)field3);
            break;
          case 2:
            this._xref[number] = new(0, (int)field2, (int)field3, 0);
            break;
        }
      }
    }

    return dict;
  }

  private static long _BigEndian(byte[] data, int at, int width) {
    long value = 0;
    for (var i = 0; i < width; ++i)
      value = (value << 8) | data[at + i];
    return value;
  }

  private static int _FindLast(byte[] data, ReadOnlySpan<byte> needle) => data.AsSpan().LastIndexOf(needle);

  /// <summary>Rebuilds the cross-reference by finding every object header in the file.</summary>
  private void _Reconstruct() {
    this._reconstructed = true;
    var data = this._data;
    PdfDict? trailer = null;
    for (var i = 0; i < data.Length - 4; ++i) {
      var b = data[i];
      if (b == (byte)'t' && PdfLexer.MatchesKeyword(data, i, "trailer"u8)) {
        var lexer = new PdfLexer(data, i + 7);
        if (lexer.ReadObject() is PdfDict t && t.ContainsKey("Root"))
          trailer = t;
        continue;
      }

      if (b != (byte)'o' || data[i + 1] != (byte)'b' || data[i + 2] != (byte)'j' || (i + 3 < data.Length && !(PdfLexer.IsWhitespace(data[i + 3]) || PdfLexer.IsDelimiter(data[i + 3]))))
        continue;

      // Walk back over "n g " to find where the header begins.
      var p = i - 1;
      while (p >= 0 && PdfLexer.IsWhitespace(data[p]))
        --p;
      var genEnd = p;
      while (p >= 0 && data[p] is >= (byte)'0' and <= (byte)'9')
        --p;
      if (p == genEnd)
        continue;
      var genStart = p + 1;
      while (p >= 0 && PdfLexer.IsWhitespace(data[p]))
        --p;
      var numEnd = p;
      while (p >= 0 && data[p] is >= (byte)'0' and <= (byte)'9')
        --p;
      if (p == numEnd || (p >= 0 && !(PdfLexer.IsWhitespace(data[p]) || PdfLexer.IsDelimiter(data[p]))))
        continue;

      var number = 0;
      for (var k = p + 1; k <= numEnd && number < 100_000_000; ++k)
        number = number * 10 + (data[k] - '0');
      var generation = 0;
      for (var k = genStart; k <= genEnd && generation < 100_000; ++k)
        generation = generation * 10 + (data[k] - '0');

      this._xref[number] = new(p + 1, -1, 0, generation);
    }

    // Objects inside object streams are found by opening every object stream that was seen.
    foreach (var (number, entry) in new List<KeyValuePair<int, XrefEntry>>(this._xref)) {
      object? obj;
      try {
        obj = this._ParseIndirectAt(entry.Offset, number, out _);
      } catch (InvalidDataException) {
        continue;
      }

      if (obj is PdfStreamObject { Dictionary: var d } && d.Raw("Type") as string == "ObjStm") {
        try {
          var stream = this._ObjectStream(number);
          for (var k = 0; k < stream.Numbers.Length; ++k)
            if (!this._xref.TryGetValue(stream.Numbers[k], out var existing) || existing.Compressed)
              this._xref[stream.Numbers[k]] = new(0, number, k, 0);
        } catch (Exception e) when (e is InvalidDataException or IndexOutOfRangeException or ArgumentException) {
          // An object stream that does not open contributes nothing.
        }
      } else if (obj is PdfStreamObject { Dictionary: var x } && x.Raw("Type") as string == "XRef" && trailer == null && x.ContainsKey("Root"))
        trailer = x;
    }

    this._cache.Clear();
    this.Trailer = trailer ?? new PdfDict();
    if (!this.Trailer.ContainsKey("Root")) {
      // No trailer survived: the catalog is whichever object says it is one.
      foreach (var (number, _) in this._xref)
        if (this.Resolve(new PdfRef(number, 0)) is PdfDict { } candidate && candidate.Raw("Type") as string == "Catalog") {
          this.Trailer["Root"] = new PdfRef(number, 0);
          break;
        }
    }
  }

  #endregion

  #region objects

  /// <summary>Follows a reference to the object it names; anything else comes back as it is.</summary>
  public object? Resolve(object? value) {
    for (var hops = 0; value is PdfRef reference && hops < 32; ++hops)
      value = this._Load(reference.ObjectNumber);

    return value is PdfRef ? null : value;
  }

  /// <summary>A value as a dictionary — a stream's own dictionary counts — or null.</summary>
  public PdfDict? Dict(object? value) => this.Resolve(value) switch {
    PdfDict d => d,
    PdfStreamObject s => s.Dictionary,
    _ => null,
  };

  /// <summary>A value as a stream, or null.</summary>
  public PdfStreamObject? Stream(object? value) => this.Resolve(value) as PdfStreamObject;

  /// <summary>A value as an array, or null.</summary>
  public List<object?>? Array(object? value) => this.Resolve(value) as List<object?>;

  /// <summary>A dictionary entry, resolved.</summary>
  public object? Get(PdfDict? dict, string key) => dict == null ? null : this.Resolve(dict.Raw(key));

  /// <summary>A dictionary entry as a number.</summary>
  public double Number(PdfDict? dict, string key, double fallback = 0) => PdfValues.Number(this.Get(dict, key), fallback);

  /// <summary>A dictionary entry as an integer.</summary>
  public int Integer(PdfDict? dict, string key, int fallback = 0) => PdfValues.Integer(this.Get(dict, key), fallback);

  /// <summary>A dictionary entry as a name.</summary>
  public string? Name(PdfDict? dict, string key) => this.Get(dict, key) as string;

  /// <summary>An array of numbers, or null when the value is not one.</summary>
  public double[]? Numbers(object? value) {
    if (this.Resolve(value) is not List<object?> list)
      return null;

    var result = new double[list.Count];
    for (var i = 0; i < list.Count; ++i)
      result[i] = PdfValues.Number(this.Resolve(list[i]));
    return result;
  }

  /// <summary>A stream's bytes with every byte filter applied; an image filter at the end is left in place.</summary>
  public byte[] Decode(PdfStreamObject stream) {
    if (stream.Decoded != null)
      return stream.Decoded;

    var decoded = PdfFilters.Decode(this, stream.Dictionary, stream.Data, out var imageFilter, out _);
    if (imageFilter != null)
      throw new NotSupportedException($"A /{imageFilter} stream was asked for as bytes.");

    stream.Decoded = decoded;
    return decoded;
  }

  private object? _Load(int number) {
    if (this._cache.TryGetValue(number, out var cached))
      return cached;
    if (!this._xref.TryGetValue(number, out var entry) || !this._resolving.Add(number))
      return null;

    object? value = null;
    try {
      if (entry.Compressed) {
        var stream = this._ObjectStream(entry.StreamNumber);
        var index = entry.Index < stream.Numbers.Length && stream.Numbers[entry.Index] == number
          ? entry.Index
          : System.Array.IndexOf(stream.Numbers, number);
        if (index >= 0) {
          var lexer = new PdfLexer(stream.Data, stream.First + stream.Offsets[index]);
          value = lexer.ReadObject();
          if (value == PdfLexer.EndOfData || value is PdfOperator)
            value = null;
        }
      } else if (entry.Offset >= 0) {
        value = this._ParseIndirectAt(entry.Offset, number, out var found);
        if (!found && this._headerOffset > 0)
          value = this._ParseIndirectAt(entry.Offset + this._headerOffset, number, out found);
        if (!found && !this._reconstructed) {
          // The table pointed at the wrong place; a scan will know better.
          this._resolving.Remove(number);
          this._RebuildAfterBadOffset();
          return this._Load(number);
        }
      }
    } catch (InvalidDataException) {
      value = null;
    } finally {
      this._resolving.Remove(number);
    }

    this._cache[number] = value;
    return value;
  }

  private void _RebuildAfterBadOffset() {
    var trailer = this.Trailer;
    this._xref.Clear();
    this._cache.Clear();
    this._objectStreams.Clear();
    this._Reconstruct();
    foreach (var (key, value) in trailer)
      this.Trailer.TryAdd(key, value);
  }

  private (int[] Numbers, int[] Offsets, int First, byte[] Data) _ObjectStream(int number) {
    if (this._objectStreams.TryGetValue(number, out var known))
      return known;

    if (this.Resolve(new PdfRef(number, 0)) is not PdfStreamObject stream)
      throw new InvalidDataException($"Object stream {number} is missing.");

    var data = this.Decode(stream);
    var n = this.Integer(stream.Dictionary, "N");
    var first = this.Integer(stream.Dictionary, "First");
    var lexer = new PdfLexer(data, 0, Math.Min(Math.Max(first, 0), data.Length));
    var numbers = new List<int>();
    var offsets = new List<int>();
    for (var i = 0; i < n; ++i) {
      if (lexer.ReadContentObject() is not int objectNumber || lexer.ReadContentObject() is not int offset)
        break;
      numbers.Add(objectNumber);
      offsets.Add(offset);
    }

    var result = (numbers.ToArray(), offsets.ToArray(), first, data);
    this._objectStreams[number] = result;
    return result;
  }

  /// <summary>Reads <c>n g obj … endobj</c> at an offset.</summary>
  private object? _ParseIndirectAt(long offset, int expected, out bool found) {
    found = false;
    if (offset < 0 || offset >= this._data.Length)
      return null;

    var lexer = new PdfLexer(this._data, (int)offset);
    if (lexer.ReadContentObject() is not int number || lexer.ReadContentObject() is not int generation || lexer.ReadContentObject() is not PdfOperator { Name: "obj" })
      return null;
    if (expected >= 0 && number != expected)
      return null;

    found = true;
    var value = lexer.ReadObject();
    if (value == PdfLexer.EndOfData)
      return null;

    if (value is PdfDict dict) {
      var save = lexer.Position;
      lexer.SkipWhitespace();
      if (PdfLexer.MatchesKeyword(this._data, lexer.Position, "stream"u8)) {
        var start = lexer.Position + 6;
        if (start < this._data.Length && this._data[start] == 13)
          ++start;
        if (start < this._data.Length && this._data[start] == 10)
          ++start;

        var bytes = this._StreamBytes(dict, start);
        var stream = new PdfStreamObject(dict, bytes) { ObjectNumber = number, Generation = generation };
        if (this._security != null && number != this._encryptObject && dict.Raw("Type") as string != "XRef") {
          this._DecryptStrings(dict, number, generation);
          stream.Data = this._security.DecryptStream(bytes, number, generation, this._CryptFilterOf(dict));
        }

        return stream;
      }

      lexer.Position = save;
    }

    if (value is PdfOperator)
      return null;

    if (this._security != null && number != this._encryptObject)
      value = this._DecryptStrings(value, number, generation);

    return value;
  }

  private byte[] _StreamBytes(PdfDict dict, int start) {
    var data = this._data;
    var declared = dict.Raw("Length");
    var length = -1L;
    if (PdfValues.IsNumber(declared))
      length = (long)PdfValues.Number(declared);
    else if (declared is PdfRef)
      length = (long)PdfValues.Number(this.Resolve(declared), -1);

    if (length >= 0 && start + length <= data.Length) {
      // Trust the length when "endstream" follows it, give or take an end of line.
      var after = new PdfLexer(data, (int)(start + length));
      after.SkipWhitespace();
      if (PdfLexer.MatchesKeyword(data, after.Position, "endstream"u8) || PdfLexer.MatchesKeyword(data, after.Position, "endobj"u8))
        return data.AsSpan(start, (int)length).ToArray();
    }

    var end = data.AsSpan(start).IndexOf("endstream"u8);
    var stop = end < 0 ? data.Length : start + end;
    var endobj = data.AsSpan(start, stop - start).IndexOf("endobj"u8);
    if (endobj >= 0)
      stop = start + endobj;

    // The end of line before "endstream" belongs to the syntax, not to the data.
    if (stop > start && data[stop - 1] == 10)
      --stop;
    if (stop > start && data[stop - 1] == 13)
      --stop;

    return data.AsSpan(start, stop - start).ToArray();
  }

  #endregion

  #region encryption

  private void _OpenEncryption() {
    var encryptRef = this.Trailer.Raw("Encrypt");
    if (encryptRef == null)
      return;

    if (encryptRef is PdfRef r)
      this._encryptObject = r.ObjectNumber;

    if (this.Resolve(encryptRef) is not PdfDict encrypt)
      return;

    byte[]? id = null;
    if (this.Resolve(this.Trailer.Raw("ID")) is List<object?> ids && ids.Count > 0)
      id = this.Resolve(ids[0]) as byte[];

    this._security = PdfSecurityHandler.Open(this, encrypt, id);

    // Whatever was parsed before the key was known was parsed in the clear; read it again.
    var keep = this._encryptObject >= 0 && this._cache.TryGetValue(this._encryptObject, out var e) ? e : null;
    this._cache.Clear();
    this._objectStreams.Clear();
    if (this._encryptObject >= 0)
      this._cache[this._encryptObject] = keep;
    this._LoadCatalog();
  }

  private string? _CryptFilterOf(PdfDict dict) {
    var filter = dict.Raw("Filter");
    var isCrypt = filter is "Crypt" || (filter is List<object?> list && list.Count > 0 && list[0] is "Crypt");
    if (!isCrypt)
      return null;

    var parms = dict.Raw("DecodeParms");
    if (parms is List<object?> pl && pl.Count > 0)
      parms = pl[0];
    return (this.Resolve(parms) as PdfDict)?.Raw("Name") as string ?? "Identity";
  }

  private object? _DecryptStrings(object? value, int number, int generation) {
    switch (value) {
      case byte[] bytes:
        return this._security!.DecryptString(bytes, number, generation);
      case List<object?> list:
        for (var i = 0; i < list.Count; ++i)
          list[i] = this._DecryptStrings(list[i], number, generation);
        return list;
      case PdfDict dict:
        foreach (var key in new List<string>(dict.Keys))
          dict[key] = this._DecryptStrings(dict[key], number, generation);
        return dict;
      default:
        return value;
    }
  }

  #endregion

  #region pages

  private bool _LoadCatalog() {
    if (this.Resolve(this.Trailer.Raw("Root")) is not PdfDict catalog)
      return false;

    this.Catalog = catalog;
    return true;
  }

  private List<PdfPageNode> _CollectPages() {
    var pages = new List<PdfPageNode>();
    var root = this.Catalog.Raw("Pages");
    var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
    this._WalkPages(root, new PageInheritance(null, null, null, null), pages, visited, 0);
    return pages;
  }

  private readonly record struct PageInheritance(object? Resources, object? MediaBox, object? CropBox, object? Rotate);

  private void _WalkPages(object? nodeRef, PageInheritance inherited, List<PdfPageNode> pages, HashSet<object> visited, int depth) {
    if (depth > 64 || this.Resolve(nodeRef) is not PdfDict node || !visited.Add(node))
      return;

    var here = new PageInheritance(
      node.Raw("Resources") ?? inherited.Resources,
      node.Raw("MediaBox") ?? inherited.MediaBox,
      node.Raw("CropBox") ?? inherited.CropBox,
      node.Raw("Rotate") ?? inherited.Rotate);

    var type = node.Raw("Type") as string;
    var kids = this.Resolve(node.Raw("Kids")) as List<object?>;
    if (type == "Page" || (type != "Pages" && kids == null)) {
      pages.Add(this._MakePage(node, here));
      return;
    }

    if (kids == null)
      return;

    foreach (var kid in kids)
      this._WalkPages(kid, here, pages, visited, depth + 1);
  }

  private List<PdfPageNode> _ScanForPages() {
    var pages = new List<PdfPageNode>();
    var numbers = new List<int>(this._xref.Keys);
    numbers.Sort();
    foreach (var number in numbers)
      if (this.Resolve(new PdfRef(number, 0)) is PdfDict d && d.Raw("Type") as string == "Page")
        pages.Add(this._MakePage(d, new(this._Inherited(d, "Resources"), this._Inherited(d, "MediaBox"), this._Inherited(d, "CropBox"), this._Inherited(d, "Rotate"))));

    return pages;
  }

  private object? _Inherited(PdfDict node, string key) {
    for (var i = 0; i < 64 && node != null; ++i) {
      if (node.Raw(key) is { } value)
        return value;
      node = this.Resolve(node.Raw("Parent")) as PdfDict ?? null!;
    }

    return null;
  }

  private PdfPageNode _MakePage(PdfDict node, PageInheritance inherited) {
    var media = _Rectangle(this.Numbers(inherited.MediaBox)) ?? new PdfBox(0, 0, 612, 792);
    var crop = _Rectangle(this.Numbers(inherited.CropBox)) ?? media;

    // The crop box is clipped to the media box (§14.11.2); one that misses it entirely is ignored.
    var clipped = new PdfBox(Math.Max(crop.Left, media.Left), Math.Max(crop.Bottom, media.Bottom), Math.Min(crop.Right, media.Right), Math.Min(crop.Top, media.Top));
    if (clipped.Right - clipped.Left <= 0 || clipped.Top - clipped.Bottom <= 0)
      clipped = media;

    var rotate = PdfValues.Integer(this.Resolve(inherited.Rotate), 0) % 360;
    if (rotate < 0)
      rotate += 360;
    rotate = rotate / 90 * 90;

    var userUnit = PdfValues.Number(this.Resolve(node.Raw("UserUnit")), 1);
    if (!(userUnit > 0) || !double.IsFinite(userUnit))
      userUnit = 1;

    return new PdfPageNode(node, this.Resolve(inherited.Resources) as PdfDict, media, clipped, rotate, userUnit);
  }

  private static PdfBox? _Rectangle(double[]? values) {
    if (values is not { Length: >= 4 })
      return null;

    var box = new PdfBox(Math.Min(values[0], values[2]), Math.Min(values[1], values[3]), Math.Max(values[0], values[2]), Math.Max(values[1], values[3]));
    return box.Right - box.Left > 0 && box.Top - box.Bottom > 0 && double.IsFinite(box.Right - box.Left) && double.IsFinite(box.Top - box.Bottom) ? box : null;
  }

  #endregion
}

/// <summary>A page as the tree describes it, with every inherited attribute filled in.</summary>
internal sealed record PdfPageNode(PdfDict Dictionary, PdfDict? Resources, PdfBox MediaBox, PdfBox CropBox, int Rotate, double UserUnit);
