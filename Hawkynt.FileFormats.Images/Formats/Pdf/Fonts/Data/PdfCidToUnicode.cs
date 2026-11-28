using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using FileFormat.Pdf.Rendering;

namespace FileFormat.Pdf.Fonts.Data;

/// <summary>
/// The Unicode values a CID of one of Adobe's four CJK character collections stands for, as a font
/// that is reached through Unicode needs them to stand in for a CID font the document does not carry.
/// </summary>
/// <remarks>
/// Three sources, consulted in the order poppler's <c>GfxCIDFont::getCodeToGIDMap</c> consults them
/// (the order is the specification here; no code is taken from it):
/// <list type="number">
/// <item>The collection's horizontal Unicode CMaps, <c>Uni…-UTF32-H</c> then <c>Uni…-UCS2-H</c>,
/// turned around: for each CID the first two distinct codes that map to it, in ascending code order.</item>
/// <item>The vertical ones, <c>Uni…-UTF32-V</c> then <c>Uni…-UCS2-V</c>, the same way, keeping one.
/// They reach the vertical-form CIDs, whose Unicode value is the horizontal character and whose glyph
/// is that character's vertical alternate.</item>
/// <item>Adobe's ToUnicode CMap of the collection (<c>Adobe-Japan1-UCS2</c> and its kin, from
/// adobe-type-tools/mapping-resources-pdf, BSD-3-Clause, packed into <c>CMaps/cid2unicode.bin.br</c>
/// by <c>CMaps/pack_cid_unicode.py</c>), which covers CIDs no encoding reaches, such as the
/// proportional and alternate forms; only CIDs it maps to a single character count.</item>
/// </list>
/// The Unicode CMaps are the bundled ones of <see cref="PdfPredefinedCMaps"/>. A table is built once
/// per collection, the first time a document needs it.
/// </remarks>
internal sealed class PdfCidToUnicode {

  private const string _Resource = "FileFormat.Pdf.Fonts.Data.CMaps.cid2unicode.bin.br";

  private static readonly Lazy<Dictionary<string, (int Cid, int Length, int Unicode)[]>> _Adobe = new(_LoadAdobe);
  private static readonly ConcurrentDictionary<string, Lazy<PdfCidToUnicode?>> _Tables = new(StringComparer.Ordinal);

  private readonly int[] _horizontal;
  private readonly int[] _vertical;
  private readonly int[] _adobe;

  private PdfCidToUnicode(string ordering, int[] horizontal, int[] vertical, int[] adobe) {
    this.Ordering = ordering;
    this._horizontal = horizontal;
    this._vertical = vertical;
    this._adobe = adobe;
  }

  /// <summary>The collection's ordering: Japan1, GB1, CNS1 or Korea1.</summary>
  public string Ordering { get; }

  /// <summary>How many CIDs the table has room for: every 16-bit one.</summary>
  public int CidCount => this._adobe.Length;

  /// <summary>The orderings a table exists for.</summary>
  public static IReadOnlyList<string> Orderings { get; } = ["Japan1", "GB1", "CNS1", "Korea1"];

  /// <summary>The table of an Adobe collection's ordering, or null for any other ordering.</summary>
  public static PdfCidToUnicode? For(string ordering)
    => _Prefix(ordering) == null ? null : _Tables.GetOrAdd(ordering, static o => new(() => _Build(o))).Value;

  /// <summary>The OpenType script and language system tags whose vertical alternates suit a collection.</summary>
  public static (string Script, string Language) LayoutTags(string ordering) => ordering switch {
    "Japan1" => ("kana", "JAN "),
    "Korea1" => ("hang", "KOR "),
    _ => ("hani", "CHN "),
  };

  /// <summary>The first or second character the horizontal Unicode CMaps give a CID, or 0.</summary>
  public int Horizontal(int cid, int candidate) {
    var at = cid * 2 + candidate;
    return (uint)cid < (uint)this.CidCount && (uint)candidate < 2 && at < this._horizontal.Length ? this._horizontal[at] : 0;
  }

  /// <summary>The character the vertical Unicode CMaps give a CID, or 0.</summary>
  public int Vertical(int cid) => (uint)cid < (uint)this._vertical.Length ? this._vertical[cid] : 0;

  /// <summary>The character Adobe's ToUnicode CMap of the collection gives a CID, or 0.</summary>
  public int Adobe(int cid) => (uint)cid < (uint)this._adobe.Length ? this._adobe[cid] : 0;

  private static string? _Prefix(string ordering) => ordering switch {
    "Japan1" => "UniJIS",
    "GB1" => "UniGB",
    "CNS1" => "UniCNS",
    "Korea1" => "UniKS",
    _ => null,
  };

  private static PdfCidToUnicode? _Build(string ordering) {
    var prefix = _Prefix(ordering)!;
    if (!_Adobe.Value.TryGetValue(ordering, out var runs))
      return null;

    // CIDs are 16-bit; no collection has more than 65536.
    const int count = 65536;
    var horizontal = new int[count * 2];
    var vertical = new int[count];
    var adobe = new int[count];
    foreach (var (cid, length, unicode) in runs)
      for (var k = 0; k < length && cid + k < count; ++k)
        adobe[cid + k] = unicode + k;

    foreach (var suffix in new[] { "-UTF32-H", "-UCS2-H" })
      _Reverse(prefix + suffix, horizontal, 2);
    foreach (var suffix in new[] { "-UTF32-V", "-UCS2-V" })
      _Reverse(prefix + suffix, vertical, 1);

    return new PdfCidToUnicode(ordering, horizontal, vertical, adobe);
  }

  // Each CID collects up to `candidates` distinct codes of the CMap, smallest first; a slot already
  // filled by an earlier CMap stays. A UCS-2 code is a UTF-16 unit, a UTF-32 one a scalar value.
  private static void _Reverse(string name, int[] target, int candidates) {
    if (PdfCMap.Predefined(name) is not { } cmap)
      return;

    var codes = new SortedSet<(int Length, uint Code)>();
    foreach (var (code, length, _) in cmap.Mappings())
      codes.Add((length, code));

    var limit = target.Length / candidates;
    foreach (var (length, code) in codes) {
      var cid = cmap.Cid(code, length);
      if (code == 0 || code > 0x10FFFF || (uint)cid >= (uint)limit)
        continue;

      for (var slot = 0; slot < candidates; ++slot) {
        ref var entry = ref target[cid * candidates + slot];
        if (entry == 0) {
          entry = (int)code;
          break;
        }

        if (entry == (int)code)
          break;
      }
    }
  }

  private static Dictionary<string, (int, int, int)[]> _LoadAdobe() {
    byte[] data;
    using (var stream = typeof(PdfCidToUnicode).Assembly.GetManifestResourceStream(_Resource)
                        ?? throw new InvalidOperationException("The bundled CID-to-Unicode maps are missing from the assembly."))
    using (var brotli = new BrotliStream(stream, CompressionMode.Decompress))
    using (var output = new MemoryStream()) {
      brotli.CopyTo(output);
      data = output.ToArray();
    }

    if (data.Length < 4 || data[0] != 'P' || data[1] != 'C' || data[2] != 'U' || data[3] != '1')
      throw new InvalidDataException("The bundled CID-to-Unicode maps have an unknown format.");

    var position = 4;
    var all = new Dictionary<string, (int, int, int)[]>(StringComparer.Ordinal);
    var collections = (int)_Varint(data, ref position);
    for (var c = 0; c < collections; ++c) {
      var nameLength = (int)_Varint(data, ref position);
      var name = Encoding.ASCII.GetString(data, position, nameLength);
      position += nameLength;
      var runs = new (int, int, int)[(int)_Varint(data, ref position)];
      long previousCid = -1, previousUnicode = -1;
      for (var i = 0; i < runs.Length; ++i) {
        var cid = previousCid + 1 + _Zigzag(data, ref position);
        var length = (long)_Varint(data, ref position) + 1;
        var unicode = previousUnicode + 1 + _Zigzag(data, ref position);
        runs[i] = ((int)cid, (int)length, (int)unicode);
        previousCid = cid + length - 1;
        previousUnicode = unicode + length - 1;
      }

      all[name] = runs;
    }

    return all;
  }

  private static ulong _Varint(byte[] data, ref int position) {
    ulong value = 0;
    var shift = 0;
    while (true) {
      var b = data[position++];
      value |= (ulong)(b & 0x7F) << shift;
      if ((b & 0x80) == 0)
        return value;
      shift += 7;
    }
  }

  private static long _Zigzag(byte[] data, ref int position) {
    var v = _Varint(data, ref position);
    return (long)(v >> 1) ^ -(long)(v & 1);
  }
}
